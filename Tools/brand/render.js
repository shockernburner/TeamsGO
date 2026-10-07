const { chromium } = require(process.env.PW);
const fs = require('fs'), path = require('path');
(async () => {
  const b = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const p = await b.newPage({ deviceScaleFactor: 2 });
  for (const f of fs.readdirSync('svg').filter(f => f.endsWith('.svg'))) {
    const svg = fs.readFileSync('svg/' + f, 'utf8');
    const m = svg.match(/width="(\d+)" height="(\d+)"/); const w = +m[1], h = +m[2];
    const scale = f.startsWith('Icon') ? 0.5 : 1;  // icon: 1024 px exactly
    await p.setViewportSize({ width: Math.round(w*scale), height: Math.round(h*scale) });
    fs.writeFileSync('svg/' + f.replace('.svg', '.html'), `<html><body style="margin:0;background:transparent">${svg.replace('<svg ', `<svg style="width:${w*scale}px;height:${h*scale}px;display:block" `)}</body></html>`);
    await p.goto('file://' + path.resolve('svg/' + f.replace('.svg', '.html')));
    await p.evaluate(() => document.fonts.ready);
    await p.waitForTimeout(150);
    await p.screenshot({ path: 'png/' + f.replace('.svg', '.png'), omitBackground: true });
  }
  await b.close();
})();
