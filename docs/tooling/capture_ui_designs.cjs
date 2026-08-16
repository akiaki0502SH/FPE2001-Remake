const path = require('path');
const { pathToFileURL } = require('url');
const { chromium } = require('C:/Users/猪/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

const root = path.resolve(__dirname, '..');
const html = path.join(root, 'deliverables', 'FPE2001_UI_Design_Kit', 'index.html');
const outDir = path.join(root, 'deliverables', 'FPE2001_UI_Design_Kit', 'screens');
const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const screens = [
  ['targets', '01-targets.png'],
  ['scan', '02-scan.png'],
  ['addresses', '03-addresses.png'],
  ['memory', '04-memory.png'],
  ['adapters', '05-adapters.png'],
  ['diagnostics', '06-diagnostics.png'],
  ['settings', '07-settings.png'],
  ['dialogs', '08-dialogs.png'],
];

(async () => {
  const browser = await chromium.launch({ executablePath: edge, headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1 });
    await page.goto(pathToFileURL(html).href, { waitUntil: 'load' });
    const frame = page.frames().find(f => f !== page.mainFrame());
    if (!frame) throw new Error('Visualization iframe was not found');
    for (const [route, file] of screens) {
      await frame.locator(`[data-route="${route}"]`).click();
      await page.waitForTimeout(120);
      await page.screenshot({ path: path.join(outDir, file), fullPage: false });
    }
  } finally {
    await browser.close();
  }
})();
