namespace SRT.Core;

// Written by "node tools/release-key.mjs new". The public half of the key that signs each release's .sha256 (base64 SubjectPublicKeyInfo,
// ECDSA P-256). The private half lives only with the maintainer, off GitHub. Empty: this build can't verify updates, so it never installs one.
public static partial class Updater
{
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEGVDBUmOYb99HSTc1SZ7HxbGL+shYoH+D+eWTwW0JZ/7PfNRsMX5/65Vb/W9NDigFoaZmjhnnTriQcf9ZnjYK1A==";
}
