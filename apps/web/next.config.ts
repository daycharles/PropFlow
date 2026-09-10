import type { NextConfig } from "next";
const config: NextConfig = {
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${process.env.PROPFLOW_API_ORIGIN ?? "https://localhost:5001"}/api/:path*`,
      },
    ];
  },
};
export default config;
