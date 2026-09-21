import { NextResponse } from "next/server";

const defaultProductionRuntime = "https://djb1ah1j5qyrj.cloudfront.net";

function runtimeBase() {
  const configured = process.env.NEXT_PUBLIC_AWS_RUNTIME_URL?.trim().replace(/\/$/, "");
  return configured || defaultProductionRuntime;
}

export async function POST() {
  const response = await fetch(`${runtimeBase()}/runtime/wake`, {
    method: "POST",
    cache: "no-store",
    signal: AbortSignal.timeout(15_000)
  });

  return new NextResponse(await response.text(), {
    status: response.status,
    headers: {
      "content-type": response.headers.get("content-type") ?? "application/json",
      ...(response.headers.has("retry-after") ? { "retry-after": response.headers.get("retry-after")! } : {})
    }
  });
}
