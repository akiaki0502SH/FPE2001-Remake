const { chromium } = require('C:/Users/猪/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path = require('path');
(async()=>{
  const input=path.resolve(process.argv[2]); const out=path.resolve(process.argv[3]);
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1600,height:1000},deviceScaleFactor:1});
  await page.goto('file:///'+input.replace(/\\/g,'/')); await page.waitForTimeout(500);
  const frames=page.frames(); const p=frames.length>1?frames[frames.length-1]:page;
  const routes=['scan','addresses','editor','files','picture','macro','speed','settings','about'];
  for(let i=0;i<routes.length;i++){await p.locator(`[data-route="${routes[i]}"]`).click();await p.waitForTimeout(120);await page.screenshot({path:path.join(out,`${String(i+1).padStart(2,'0')}-${routes[i]}.png`),fullPage:false});}
  await p.locator('[data-open-dialog]').click();await p.waitForTimeout(120);await page.screenshot({path:path.join(out,'10-dialogs.png'),fullPage:false});
  await browser.close();
})();
