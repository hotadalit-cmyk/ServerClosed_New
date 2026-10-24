import type { Metadata, Viewport } from "next";
import "./globals.css";
import RegisterSW from "@/components/game/RegisterSW";

export const metadata: Metadata = {
  title: "SERVERCLOSED — Две жизни одного слизня",
  description:
    "Играбельный прототип фэнтезийной RPG с эволюцией, автобаттлером Magic Chess, кармой и реинкарнацией. Вертикальный срез по ТЗ §20.",
  applicationName: "SERVERCLOSED",
  manifest: "/manifest.json",
  appleWebApp: {
    capable: true,
    statusBarStyle: "black-translucent",
    title: "SERVERCLOSED",
  },
  icons: {
    icon: [
      { url: "/icons/icon-192.png", sizes: "192x192", type: "image/png" },
      { url: "/icons/icon-512.png", sizes: "512x512", type: "image/png" },
    ],
    apple: [{ url: "/icons/apple-touch-icon.png", sizes: "180x180", type: "image/png" }],
  },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  maximumScale: 1,
  userScalable: false,
  viewportFit: "cover",
  themeColor: "#070907",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ru">
      <body className="antialiased">
        {children}
        <RegisterSW />
      </body>
    </html>
  );
}
