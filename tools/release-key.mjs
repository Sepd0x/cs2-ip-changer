#!/usr/bin/env node
// The key that signs releases. The app only installs an update whose .sha256 (with the version) is signed with it.
//
//   node tools/release-key.mjs new [--out <file>]     once: makes the key (asks for a passphrase), writes its public half to src/Core/UpdateKey.cs
//   node tools/release-key.mjs sign <tag> [--key <file>] [--no-publish]
//                                                      per release, after the workflow made the draft: checks the exe against its .sha256
//                                                      and its build attestation, signs, uploads CS2IPChanger.exe.sha256.sig, publishes
//
// The private key never goes in the repo or on GitHub (the tool refuses a path inside the repo, and *.pem is in .gitignore).
// Keep a copy somewhere offline: losing it means the installed apps can't update by themselves any more.
// Needs Node 18+ and the GitHub CLI (gh) logged in.

import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const REPO = 'Sepd0x/cs2-ip-changer';
const EXE = 'CS2IPChanger.exe';
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const KEY_CS = path.join(ROOT, 'src', 'Core', 'UpdateKey.cs');
const DEFAULT_KEY = path.join(os.homedir(), 'cs2ipchanger-release-key.pem');

const [cmd, ...rest] = process.argv.slice(2);
const opt = (name) => { const i = rest.indexOf(name); return i >= 0 ? rest[i + 1] : undefined; };
const flag = (name) => rest.includes(name);
const fail = (msg) => { console.error('ERROR: ' + msg); process.exit(1); };

/** The same bytes Updater.SignedMessage signs in the app. */
const message = (version, shaFile) => Buffer.concat([Buffer.from(`CS2IPChanger ${version.replace(/^v/i, '')}\n`, 'utf8'), shaFile]);

function publicKeyInRepo() {
  const m = fs.readFileSync(KEY_CS, 'utf8').match(/PublicKey\s*=\s*"([^"]*)"/);
  if (!m) fail(`no PublicKey in ${KEY_CS}`);
  return m[1];
}

// One reader for the whole run (a second one would miss lines the first already took); what you type for a passphrase isn't shown.
let rl, muted = false;
const lines = [], waiting = [];
function ask(question, hidden) {
  if (!rl) {
    rl = readline.createInterface({ input: process.stdin, output: process.stdout, terminal: !!process.stdin.isTTY });
    rl._writeToOutput = (s) => { if (!muted) rl.output.write(s); };
    rl.on('line', (l) => (waiting.length ? waiting.shift()(l) : lines.push(l)));
  }
  process.stdout.write(question);
  muted = hidden;
  return new Promise((resolve) => {
    const done = (answer) => { muted = false; if (hidden) process.stdout.write('\n'); resolve(answer); };
    lines.length ? done(lines.shift()) : waiting.push(done);
  });
}

function gh(args, { quiet = false } = {}) {
  const r = spawnSync('gh', [...args, '--repo', REPO], { encoding: 'utf8', stdio: quiet ? 'pipe' : ['ignore', 'pipe', 'inherit'] });
  if (r.error) fail('gh is not installed or not in PATH');
  return r;
}

async function newKey() {
  const out = path.resolve(opt('--out') ?? DEFAULT_KEY);
  if (out.toLowerCase().startsWith(ROOT.toLowerCase() + path.sep)) fail('the key would be inside the repo; pick a folder outside it');
  if (fs.existsSync(out)) fail(`${out} already exists`);
  if (publicKeyInRepo() && !flag('--replace')) fail('the repo already has a public key. A new one means the installed apps stop updating by themselves; add --replace if that is really what you want');
  const pass = await ask('Passphrase for the key (12+ characters): ', true);
  if (pass.length < 12) fail('passphrase too short');
  if (await ask('Again: ', true) !== pass) fail('the two passphrases are different');
  const { privateKey, publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
  fs.writeFileSync(out, privateKey.export({ type: 'pkcs8', format: 'pem', cipher: 'aes-256-cbc', passphrase: pass }), { flag: 'wx', mode: 0o600 });
  const spki = publicKey.export({ type: 'spki', format: 'der' }).toString('base64');
  fs.writeFileSync(KEY_CS, fs.readFileSync(KEY_CS, 'utf8').replace(/PublicKey\s*=\s*"[^"]*"/, `PublicKey = "${spki}"`));
  console.log(`Private key: ${out} (encrypted with your passphrase)`);
  console.log(`Public key written to ${path.relative(ROOT, KEY_CS)}: commit it before the next release.`);
  console.log('Now keep a copy of the .pem offline (a USB stick, your password manager). Without it the apps can\'t update by themselves.');
}

async function sign() {
  const tag = rest.find((a) => !a.startsWith('--') && a !== opt('--key'));
  if (!tag || !/^v\d+\.\d+\.\d+/.test(tag)) fail('usage: node tools/release-key.mjs sign v1.2.3 [--key <file>] [--no-publish]');
  const pub = publicKeyInRepo();
  if (!pub) fail('the repo has no public key yet: run "node tools/release-key.mjs new" first');

  const view = gh(['release', 'view', tag, '--json', 'isDraft,tagName']);
  if (view.status !== 0) fail(`no release ${tag} on GitHub yet (did the Release workflow finish?)`);
  const { isDraft } = JSON.parse(view.stdout);

  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cs2ipc-sign-'));
  console.log(`Downloading ${EXE} and its .sha256 from ${tag}…`);
  if (gh(['release', 'download', tag, '-p', EXE, '-p', `${EXE}.sha256`, '-D', dir], { quiet: true }).status !== 0) fail('download failed');
  const exe = path.join(dir, EXE);
  const shaFile = fs.readFileSync(path.join(dir, `${EXE}.sha256`));
  const listed = shaFile.toString('utf8').match(/^([0-9a-fA-F]{64})/m)?.[1]?.toLowerCase();
  const actual = crypto.createHash('sha256').update(fs.readFileSync(exe)).digest('hex');
  if (listed !== actual) fail(`the exe's SHA-256 (${actual}) is not the one in the .sha256 (${listed})`);

  console.log('Checking that the exe was built by this repo\'s Release workflow (attestation)…');
  const att = gh(['attestation', 'verify', exe, '--signer-workflow', `${REPO}/.github/workflows/release.yml`], { quiet: true });
  if (att.status !== 0) fail('the attestation does not check out:\n' + (att.stderr || att.stdout));

  const keyFile = path.resolve(opt('--key') ?? DEFAULT_KEY);
  if (!fs.existsSync(keyFile)) fail(`no key at ${keyFile} (use --key <file>)`);
  let key;
  try { key = crypto.createPrivateKey({ key: fs.readFileSync(keyFile), passphrase: await ask('Passphrase: ', true) }); }
  catch { fail('wrong passphrase or not a key file'); }
  const msg = message(tag, shaFile);
  const sig = crypto.sign('sha256', msg, key).toString('base64');
  const ok = crypto.verify('sha256', msg, { key: Buffer.from(pub, 'base64'), format: 'der', type: 'spki' }, Buffer.from(sig, 'base64'));
  if (!ok) fail('this key is not the one whose public half is in src/Core/UpdateKey.cs');

  const sigPath = path.join(dir, `${EXE}.sha256.sig`);
  fs.writeFileSync(sigPath, sig + '\n');
  if (gh(['release', 'upload', tag, sigPath, '--clobber']).status !== 0) fail('upload failed');
  console.log(`Signed: ${EXE}.sha256.sig uploaded to ${tag} (SHA-256 ${actual}).`);
  if (isDraft && !flag('--no-publish')) {
    if (gh(['release', 'edit', tag, '--draft=false']).status !== 0) fail('the release is signed but still a draft; publish it on GitHub');
    console.log('Published.');
  } else if (isDraft) console.log('Left as a draft (--no-publish).');
}

if (cmd === 'new') await newKey();
else if (cmd === 'sign') await sign();
else {
  console.log('node tools/release-key.mjs new [--out <file>]');
  console.log('node tools/release-key.mjs sign <tag> [--key <file>] [--no-publish]');
  process.exit(cmd ? 1 : 0);
}
rl?.close();
