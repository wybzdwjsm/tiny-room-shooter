// 本地预览 Unity WebGL 包：为 .br / .gz 补 Content-Encoding 与正确的 MIME，
// 否则浏览器会把 WebGL.wasm.br 当作普通二进制，加载直接失败。
const http = require("http");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(process.argv[2] || process.cwd());
const PORT = Number(process.argv[3] || 8788);

const MIME = {
  ".html": "text/html; charset=utf-8",
  ".js": "application/javascript",
  ".css": "text/css",
  ".json": "application/json",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
  ".wasm": "application/wasm",
  ".data": "application/octet-stream",
  ".unityweb": "application/octet-stream",
};

http
  .createServer((req, res) => {
    let rel = decodeURIComponent(req.url.split("?")[0]);
    if (rel === "/" || rel === "") rel = "/index.html";

    const file = path.resolve(path.join(ROOT, rel));
    if (!file.startsWith(ROOT)) {
      res.writeHead(403).end("forbidden");
      return;
    }

    fs.readFile(file, (err, data) => {
      if (err) {
        res.writeHead(404).end("not found: " + rel);
        return;
      }

      let ext = path.extname(file).toLowerCase();
      const headers = { "Cache-Control": "no-store" };

      if (ext === ".br" || ext === ".gz") {
        headers["Content-Encoding"] = ext === ".br" ? "br" : "gzip";
        ext = path.extname(path.basename(file, ext)).toLowerCase();
      }

      headers["Content-Type"] = MIME[ext] || "application/octet-stream";
      res.writeHead(200, headers);
      res.end(data);
    });
  })
  .listen(PORT, "127.0.0.1", () => {
    console.log("serving " + ROOT + " at http://127.0.0.1:" + PORT + "/");
  });
