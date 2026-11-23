// 键盘贴图和两端共用的键帽中心同时导出。离线重绘需要 Node.js 和 @napi-rs/canvas。
const fs = require('fs');
const path = require('path');
const { createCanvas } = require('@napi-rs/canvas');

const assetDir = path.join(__dirname, '..', 'assets');
const bounds = { width: 318, height: 98 };
const placement = { x: 86, y: 197, scale: 0.88 };
const scale = 4;
const canvas = createCanvas(bounds.width * scale, bounds.height * scale);
const pen = canvas.getContext('2d');
pen.scale(scale, scale);
pen.lineJoin = 'round';
pen.lineCap = 'round';

// 扫描码、Mac 键码、名称、宽度（标准键为 1）。竖线区分左右手。
const rows = [
  '01:53:Esc 02:18:1 03:19:2 04:20:3 05:21:4 06:23:5 | 07:22:6 08:26:7 09:28:8 0A:25:9 0B:29:0 0C:27:- 0D:24:= 0E:51:Backspace:2',
  '0F:48:Tab:1.5 10:12:Q 11:13:W 12:14:E 13:15:R 14:17:T | 15:16:Y 16:32:U 17:34:I 18:31:O 19:35:P 1A:33:[ 1B:30:] 2B:42:\\:1.5',
  '3A:57:Caps:1.75 1E:0:A 1F:1:S 20:2:D 21:3:F 22:5:G | 23:4:H 24:38:J 25:40:K 26:37:L 27:41:; 28:39:\' 1C:36:Enter:2.25',
  '2A:56:Shift:2.25 2C:6:Z 2D:7:X 2E:8:C 2F:9:V 30:11:B | 31:45:N 32:46:M 33:43:, 34:47:. 35:44:/ 36:60:Shift:2.75',
  '1D:59:Ctrl:1.25 15B:55:Win:1.25 38:58:Alt:1.25 39:49:Space:6.25 | 138:61:Alt:1.25 15C:54:Win:1.25 15D:110:Menu:1.25 11D:62:Ctrl:1.25'
];
const palette = {
  ink: '#513a27', dividers: '#876749', shellTop: '#d3d4b7', shellSide: '#a7af8d',
  cream: ['#fff2da', '#fff1d7', '#fcf0d8', '#fff3dd'],
  sage: ['#cad0af', '#c3caab', '#cfd2b6'], creamSide: '#e4cfac', sageSide: '#a9b296',
  enter: '#f3c25e', enterSide: '#d9a64b'
};
function project(u, v) {
  return [4.9 + 18.05 * u + 5.8 * v + 0.07 * u * v,
    20.3 - 0.99 * u + 12.2 * v - 0.015 * u * v];
}
function panel(u0, v0, u1, v1, radiusU, radiusV, offsetX, offsetY, fill, stroke, lineWidth) {
  const at = (u, v) => {
    const [x, y] = project(u, v);
    return [x + offsetX, y + offsetY];
  };
  pen.beginPath();
  pen.moveTo(...at(u0 + radiusU, v0));
  pen.lineTo(...at(u1 - radiusU, v0));
  pen.quadraticCurveTo(...at(u1, v0), ...at(u1, v0 + radiusV));
  pen.lineTo(...at(u1, v1 - radiusV));
  pen.quadraticCurveTo(...at(u1, v1), ...at(u1 - radiusU, v1));
  pen.lineTo(...at(u0 + radiusU, v1));
  pen.quadraticCurveTo(...at(u0, v1), ...at(u0, v1 - radiusV));
  pen.lineTo(...at(u0, v0 + radiusV));
  pen.quadraticCurveTo(...at(u0, v0), ...at(u0 + radiusU, v0));
  pen.closePath();
  pen.fillStyle = fill;
  pen.fill();
  if (stroke) { pen.strokeStyle = stroke; pen.lineWidth = lineWidth; pen.stroke(); }
}

function shell(dx, dy, fill, edge, weight) {
  panel(-0.12, -0.15, 15.15, 5.12, 0.55, 0.69, dx, dy, fill, edge, weight);
}
// 厚度只留在键盘朝向玩家的一侧，侧边与顶面连成一个外壳。
shell(1.7, 12.6, 'rgba(86, 90, 65, 0.20)');
for (let depth = 0; depth <= 9; depth++) shell(0.05 * depth, depth, palette.ink, palette.ink, 1.8);
for (let depth = 9; depth >= 0; depth--) shell(0.05 * depth, depth, palette.shellSide);
shell(0, 0.1, palette.shellTop, palette.ink, 1.6);

const geometry = ['# scan(hex) mac label hand centerX centerY width'];
// 功能键使用线稿图标，避免依赖本机字体的符号覆盖范围。
const symbols = {
  Enter: [[[3, -1.8], [3, 0.8], [-2.8, 0.8]], [[-1.1, -0.8], [-2.8, 0.8], [-1.1, 2.25]]],
  Shift: [[[-2.8, 0], [0, -2.5], [2.8, 0], [1.35, 0], [1.35, 2], [-1.35, 2], [-1.35, 0], [-2.8, 0]]],
  Backspace: [[[-3.2, 0], [-1.2, -1.9], [3, -1.9], [3, 1.9], [-1.2, 1.9], [-3.2, 0]], [[0, -0.8], [1.7, 0.8]], [[0, 0.8], [1.7, -0.8]]],
  Win: [[[0, -2], [2.4, 0], [0, 2], [-2.4, 0], [0, -2]]],
  Menu: [[[-2.2, -1.6], [2.2, -1.6]], [[-2.2, 0], [2.2, 0]], [[-2.2, 1.6], [2.2, 1.6]]]
};
function drawLegend(label, center, modifier) {
  if (label === 'Space') return;
  pen.save();
  pen.translate(...center);
  pen.rotate(Math.PI - 0.055);
  const color = modifier ? '#766747' : '#a38b69';
  if (symbols[label]) {
    pen.lineWidth = 0.55;
    pen.strokeStyle = color;
    for (const [first, ...rest] of symbols[label]) {
      pen.beginPath();
      pen.moveTo(...first);
      for (const point of rest) pen.lineTo(...point);
      pen.stroke();
    }
  } else {
    pen.fillStyle = color;
    pen.font = (label.length === 1 ? '5.3' : '4.5') + 'px "Segoe UI", sans-serif';
    pen.textAlign = 'center';
    pen.textBaseline = 'middle';
    pen.fillText(label, 0, 0.18);
  }
  pen.restore();
}
// 空格一排靠近豚鼠。按画面从后向前绘制，键帽前缘仍朝下受光。
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
    const mod = label !== 'Space' && (units > 1 || label === 'Esc');
    const accent = label === 'Enter';
    const fills = mod ? palette.sage : palette.cream;
    const face = accent ? palette.enter : fills[seed % fills.length];
    const wall = accent ? palette.enterSide : mod ? palette.sageSide : palette.creamSide;
    panel(u0, v0, u1, v1, 0.16, 0.18, 0.00, 1.42, wall, palette.ink, 1.0);
    panel(u0, v0, u1, v1, 0.16, 0.18, 0.00, 0.00, face, palette.dividers, 0.72);
    const center = project(leftEdge + units / 2, (v0 + v1) / 2);
    const left = project(u0, (v0 + v1) / 2), right = project(u1, (v0 + v1) / 2);
    const width = right[0] - left[0];
    drawLegend(label, center, mod);
    geometry.push([scan, mac, label, side, (placement.x + center[0] * placement.scale).toFixed(3),
      (placement.y + center[1] * placement.scale).toFixed(3), (width * placement.scale).toFixed(3)].join(' '));
    start += units;
  }
  if (Math.abs(start - 15) > 0.001) throw new Error('每行的总宽度必须为 15 个标准键');
});
fs.writeFileSync(path.join(assetDir, 'keyboard.png'), canvas.toBuffer('image/png'));
fs.writeFileSync(path.join(assetDir, 'keyboard-layout.txt'), geometry.join('\n') + '\n');
