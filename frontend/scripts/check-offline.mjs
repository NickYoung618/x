import { readFileSync } from 'node:fs'

const html = readFileSync(new URL('../dist/index.html', import.meta.url), 'utf8')
for (const [, resource] of html.matchAll(/(?:src|href)="([^"]+)"/g)) {
  if (!resource.startsWith('/') && !resource.startsWith('./')) {
    throw new Error(`External page resource is not allowed: ${resource}`)
  }
}
if (!html.includes('/assets/')) throw new Error('Built page has no local assets')
console.log('PASS: page entry uses local assets only')
