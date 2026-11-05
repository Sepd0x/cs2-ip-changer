Scripts served by the site itself (no third-party CDN, no cookies).

readout.js        The site's own motion: the IP readout, the six pages, the ban timeline, the guides' numbers.
scenes.min.js     The two 3D scenes: the cover (proposal A's route) and "How it works". Sources: ../src/scenes.js,
                  cover.js and route.js (three.js r186, built with esbuild; the build command is at the top of scenes.js). three.js is MIT licensed: https://github.com/mrdoob/three.js
gsap.min.js       GSAP 3.15.0 and ScrollTrigger.min.js, under the GSAP Standard License: https://gsap.com/standard-license
ScrollTrigger.min.js
lenis.min.js      Lenis 1.3.26, MIT licensed: https://github.com/darkroomengineering/lenis
