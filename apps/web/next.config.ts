import type { NextConfig } from "next";

/**
 * Unity WebGL 번들을 그대로 내주기 위한 설정.
 *
 * Unity는 압축 방식을 **확장자로만** 알린다(`.br`). 서버가 `Content-Encoding: br`을
 * 붙여주지 않으면 브라우저가 압축을 못 풀고, 로더는 "Unable to parse Build/xxx.br"로
 * 죽는다. `Content-Type`도 여기서 정해야 한다 — `application/octet-stream`으로
 * 나가면 `WebAssembly.compile`이 거절한다.
 *
 * 압축 해제 폴백(decompressionFallback)을 켜면 헤더 없이도 돌지만, 그 대신
 * 번들에 해제기가 실려 첫 다운로드가 커진다. §1.2 예산 25MB를 지키는 쪽을 골랐다.
 */
const nextConfig: NextConfig = {
  async headers() {
    return [
      {
        source: "/game/Build/:file*.wasm.br",
        headers: [
          { key: "Content-Encoding", value: "br" },
          { key: "Content-Type", value: "application/wasm" },
        ],
      },
      {
        source: "/game/Build/:file*.js.br",
        headers: [
          { key: "Content-Encoding", value: "br" },
          { key: "Content-Type", value: "application/javascript" },
        ],
      },
      {
        source: "/game/Build/:file*.data.br",
        headers: [
          { key: "Content-Encoding", value: "br" },
          { key: "Content-Type", value: "application/octet-stream" },
        ],
      },
    ];
  },
};

export default nextConfig;
