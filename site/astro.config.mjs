// @ts-check
import { defineConfig } from 'astro/config';

// Project page on GitHub Pages: https://zbcoding.github.io/ImpastoPaint/
export default defineConfig({
  site: 'https://zbcoding.github.io',
  base: '/ImpastoPaint',
  // 'ignore' (Astro's default) accepts a request with or without the
  // trailing slash instead of 404ing/redirecting on the one it doesn't
  // expect — matters because GitHub Pages serves directory-style output
  // (site/es/index.html) at both /es and /es/.
  trailingSlash: 'ignore',
});
