import { z } from "zod";
import type { components } from "@/lib/api-schema";

export const taskSchema: z.ZodType<components["schemas"]["RecurringTaskDto"]> =
  z.object({
    id: z.string().uuid(),
    name: z.string(),
    intervalDays: z.number().int(),
    startDate: z.iso.date(),
    version: z.number().int(),
    isDue: z.boolean(),
    completed: z.boolean(),
    canComplete: z.boolean(),
  });

export const taskListSchema: z.ZodType<components["schemas"]["TaskListDto"]> =
  z.object({
    today: z.iso.date(),
    date: z.iso.date(),
    items: z.array(taskSchema),
  });

export type RecurringTask = components["schemas"]["RecurringTaskDto"];
export type CreateTask = components["schemas"]["CreateTaskRequest"];
export type RenameTask = components["schemas"]["RenameTaskRequest"];
export type TaskCompletion = components["schemas"]["TaskCompletionRequest"];

const version = z.number().int();
const id = z.string().uuid();
export const pendingTaskSchema = z.discriminatedUnion("kind", [
  z.object({
    kind: z.literal("create"),
    input: z.object({
      requestId: id,
      name: z.string(),
      intervalDays: z.number().int(),
      startDate: z.iso.date(),
    }),
  }),
  z.object({
    kind: z.literal("rename"),
    id,
    input: z.object({ name: z.string(), version }),
  }),
  z.object({
    kind: z.literal("complete"),
    id,
    input: z.object({ date: z.iso.date(), completed: z.boolean(), version }),
  }),
  z.object({ kind: z.literal("delete"), id, version }),
]);
export type PendingTask = z.infer<typeof pendingTaskSchema>;
