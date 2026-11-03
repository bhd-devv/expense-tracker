import type { Metadata } from "next";
import Tasks from "@/features/tasks/tasks";

export const metadata: Metadata = {
  title: "Lime · Завдання / Tasks",
  description: "Щоденні та регулярні завдання. Daily and recurring tasks.",
};

export default function Page() {
  return <Tasks />;
}
