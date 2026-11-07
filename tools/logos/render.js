// Renders every .logo and .icon in logos.html to GameData/KerbalNews/Sites/logos/<id>.png (2x, transparent),
// and prints each one's size in CSS pixels for the templates. Run: node tools/logos/render.js
const { chromium } = require('playwright');
const path = require('path'), fs = require('fs');
(async () => {
  const out = path.join(__dirname, '../../GameData/KerbalNews/Sites/logos');
  fs.mkdirSync(out, { recursive: true });
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1400, height: 1000 }, deviceScaleFactor: 2 });
  await p.goto('file://' + path.join(__dirname, 'logos.html'));
  await p.evaluate(() => document.fonts.ready);
  await p.addStyleTag({ content: 'body { background: transparent !important; }' });
  for (const el of await p.$$('.logo, .icon')) {
    const id = await el.getAttribute('id');
    const box = await el.boundingBox();
    await el.screenshot({ path: path.join(out, id + '.png'), omitBackground: true });
    console.log(id, Math.round(box.width) + 'x' + Math.round(box.height));
  }
  await b.close();
})();
