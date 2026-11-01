import { z } from "zod";
import type { components } from "./api-schema";
import { telegram } from "./telegram";

const id = z.string().uuid();
export const profileSchema: z.ZodType<components["schemas"]["ProfileDto"]> =
  z.object({
    id,
    language: z.string(),
    theme: z.string(),
    timeZone: z.string(),
    currency: z.string(),
    aiConsent: z.boolean(),
    version: z.number().int(),
    today: z.string(),
  });
export const itemSchema: z.ZodType<components["schemas"]["CatalogItemDto"]> =
  z.object({
    id,
    name: z.string(),
    code: z.string().nullable(),
    archived: z.boolean(),
    version: z.number().int(),
  });
export const catalogSchema: z.ZodType<components["schemas"]["CatalogDto"]> =
  z.object({
    categories: z.array(itemSchema),
  });
export const expenseSchema: z.ZodType<components["schemas"]["ExpenseDto"]> =
  z.object({
    id,
    amount: z.string(),
    currency: z.string(),
    description: z.string(),
    date: z.string(),
    categoryId: id,
    source: z.string(),
    version: z.number().int(),
  });
export const pageSchema: z.ZodType<components["schemas"]["ExpensePage"]> =
  z.object({
    items: z.array(expenseSchema),
    page: z.number().int(),
    hasMore: z.boolean(),
  });
export const draftSchema: z.ZodType<components["schemas"]["DraftDto"]> =
  z.object({
    id,
    status: z.string(),
    amount: z.string().nullable(),
    currency: z.string(),
    description: z.string(),
    transcript: z.string(),
    date: z.string(),
    categoryId: id.nullable(),
    errorCode: z.string().nullable(),
    version: z.number().int(),
    expenseId: id.nullable(),
  });
export const reportSchema: z.ZodType<components["schemas"]["ReportDto"]> =
  z.object({
    totals: z.array(
      z.object({
        currency: z.string(),
        amount: z.string(),
        count: z.number().int(),
      }),
    ),
    categories: z.array(
      z.object({
        currency: z.string(),
        categoryId: id,
        amount: z.string(),
        count: z.number().int(),
      }),
    ),
  });
export type Profile = components["schemas"]["ProfileDto"];
export type Catalog = z.infer<typeof catalogSchema>;
export type Item = z.infer<typeof itemSchema>;
export type Expense = z.infer<typeof expenseSchema>;
export type Draft = z.infer<typeof draftSchema>;
export type ExpenseInput = components["schemas"]["ExpenseInput"];
export class ApiError extends Error {
  constructor(
    public code: string,
    public status: number,
  ) {
    super(code);
  }
}
let csrf = "";
export async function request<T>(
  path: string,
  schema: z.ZodType<T>,
  method = "GET",
  body?: unknown,
): Promise<T> {
  if (method !== "GET" && !csrf) await refreshCsrf();
  const form = body instanceof FormData;
  const response = await fetch(`/api${path}`, {
    method,
    credentials: "same-origin",
    cache: "no-store",
    headers: {
      ...(body !== undefined && !form
        ? { "Content-Type": "application/json" }
        : {}),
      ...(method !== "GET" ? { "X-CSRF-Token": csrf } : {}),
    },
    body: body === undefined ? undefined : form ? body : JSON.stringify(body),
  }).catch(() => {
    throw new ApiError(method === "GET" ? "connection" : "unknown", 0);
  });
  if (!response.ok) {
    const error = z
      .object({ code: z.string().optional() })
      .safeParse(await response.json().catch(() => null));
    throw new ApiError(
      error.success ? (error.data.code ?? "connection") : "connection",
      response.status,
    );
  }
  return schema.parse(
    response.status === 204 ? undefined : await response.json(),
  );
}
export const nothing = z.undefined();
async function refreshCsrf() {
  const value = await request("/auth/csrf", z.object({ token: z.string() }));
  csrf = value.token;
}
let bootstrap: Promise<Profile> | undefined;
export function authenticate() {
  bootstrap ??= (async () => {
    telegram()?.ready();
    telegram()?.expand();
    try {
      return await request("/auth/me", profileSchema);
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 401) throw error;
    }
    const app = telegram();
    if (!app?.initData) throw new ApiError("telegram_required", 401);
    app.ready();
    app.expand();
    const profile = await request("/auth/telegram", profileSchema, "POST", {
      initData: app.initData,
    });
    await refreshCsrf();
    return profile;
  })();
  bootstrap.catch(() => {
    bootstrap = undefined;
  });
  return bootstrap;
}
