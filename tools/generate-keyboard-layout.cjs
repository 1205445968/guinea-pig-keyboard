// 生成两端共用的键位布局。
const fs = require('fs');
const path = require('path');

const assetDir = path.join(__dirname, '..', 'assets');
const placement = { x: 86, y: 197, scale: 0.88 };

// 扫描码、Mac 键码、名称、宽度（标准键为 1）。竖线区分左右手。
const rows = [
  '01:53:Esc 02:18:1 03:19:2 04:20:3 05:21:4 06:23:5 | 07:22:6 08:26:7 09:28:8 0A:25:9 0B:29:0 0C:27:- 0D:24:= 0E:51:Backspace:2',
  '0F:48:Tab:1.5 10:12:Q 11:13:W 12:14:E 13:15:R 14:17:T | 15:16:Y 16:32:U 17:34:I 18:31:O 19:35:P 1A:33:[ 1B:30:] 2B:42:\\:1.5',
  '3A:57:Caps:1.75 1E:0:A 1F:1:S 20:2:D 21:3:F 22:5:G | 23:4:H 24:38:J 25:40:K 26:37:L 27:41:; 28:39:\' 1C:36:Enter:2.25',
  '2A:56:Shift:2.25 2C:6:Z 2D:7:X 2E:8:C 2F:9:V 30:11:B | 31:45:N 32:46:M 33:43:, 34:47:. 35:44:/ 36:60:Shift:2.75',
  '1D:59:Ctrl:1.25 15B:55:Win:1.25 38:58:Alt:1.25 39:49:Space:6.25 | 138:61:Alt:1.25 15C:54:Win:1.25 15D:110:Menu:1.25 11D:62:Ctrl:1.25'
];
function project(u, v) {
  return [4.9 + 18.05 * u + 5.8 * v + 0.07 * u * v,
    20.3 - 0.99 * u + 12.2 * v - 0.015 * u * v];
}
const geometry = ['# scan(hex) mac label hand centerX centerY width'];
rows.slice().reverse().forEach((row, rowIndex) => {
  let start = 0, side = 'L';
  for (const token of row.split(' ')) {
    if (token === '|') { side = 'R'; continue; }
    const [scan, mac, label, size] = token.split(':');
    const units = Number(size || 1);
    const seed = parseInt(scan, 16);
    const inset = 0.065;
    const leftEdge = 15 - start - units;
    const u0 = leftEdge + inset, u1 = leftEdge + units - inset;
    const v0 = rowIndex + 0.085 + Math.sin(seed * 4.7) * 0.017;
    const v1 = rowIndex + 0.90 + Math.cos(seed * 2.3) * 0.012;
    const center = project(leftEdge + units / 2, (v0 + v1) / 2);
    const left = project(u0, (v0 + v1) / 2), right = project(u1, (v0 + v1) / 2);
    const width = right[0] - left[0];
    geometry.push([scan, mac, label, side, (placement.x + center[0] * placement.scale).toFixed(3),
      (placement.y + center[1] * placement.scale).toFixed(3), (width * placement.scale).toFixed(3)].join(' '));
    start += units;
  }
  if (Math.abs(start - 15) > 0.001) throw new Error('每行的总宽度必须为 15 个标准键');
});
fs.writeFileSync(path.join(assetDir, 'keyboard-layout.txt'), geometry.join('\n') + '\n');
