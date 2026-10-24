import { NextResponse } from "next/server";

// force-static: маршрут тривиален, а статическая сборка (output: "export",
// STATIC_EXPORT=1) требует статических route handlers.
export const dynamic = "force-static";

export async function GET() {
  return NextResponse.json({ message: "Hello, world!" });
}
