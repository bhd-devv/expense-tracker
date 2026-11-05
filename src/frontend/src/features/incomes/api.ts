import { z } from "zod";
import type { components } from "@/lib/api-schema";

export const incomeSchema: z.ZodType<components["schemas"]["IncomeDto"]> =
  z.object({
    id: z.string().uuid(),
    amount: z.string(),
    currency: z.string(),
    categoryId: z.string().uuid(),
    description: z.string(),
    date: z.string(),
    version: z.number().int(),
  });
export const incomePageSchema: z.ZodType<components["schemas"]["IncomePage"]> =
  z.object({
    items: z.array(incomeSchema),
    page: z.number().int(),
    hasMore: z.boolean(),
    total: z.string(),
    currency: z.string(),
  });
export type Income = z.infer<typeof incomeSchema>;
export const pendingSchema = z.object({
  resource: z.enum(["incomes", "income-categories"]),
  id: z.string().uuid().optional(),
  method: z.enum(["POST", "PUT", "DELETE"]),
  version: z.number().int().optional(),
  body: z
    .record(z.string(), z.union([z.string(), z.number(), z.null()]))
    .optional(),
});
export type Pending = z.infer<typeof pendingSchema>;
