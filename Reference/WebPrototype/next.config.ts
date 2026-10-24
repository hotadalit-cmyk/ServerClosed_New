import type { NextConfig } from "next";

// STATIC_EXPORT=1 — офлайн-сборка для телефона (каталог out/, одиночный HTML).
// Обычный запуск платформы — standalone, как раньше.
const isStatic = process.env.STATIC_EXPORT === "1";

const nextConfig: NextConfig = {
  output: isStatic ? "export" : "standalone",
  ...(isStatic ? { images: { unoptimized: true } } : {}),
  typescript: {
    ignoreBuildErrors: true,
  },
  reactStrictMode: false,
};

export default nextConfig;
