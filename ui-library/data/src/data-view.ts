import { DataColumn } from "./data-column.js";
import { DataRow, type DataRowState } from "./data-row.js";
import { DataTable } from "./data-table.js";

export class DataView {
  readonly table: DataTable;
  rowFilter: string | ((row: DataRow) => boolean) | null = null;
  sort: string | null = null;
  rowStateFilter: DataRowState[] = ["Unchanged", "Added", "Modified"];

  constructor(table: DataTable) {
    this.table = table;
  }

  get count(): number {
    return this.toRows().length;
  }

  toRows(): DataRow[] {
    const rawRows = this.table.rows.toArray(true).filter((r) => this.rowStateFilter.includes(r.rowState));

    let filtered = rawRows;
    if (this.rowFilter) {
      if (typeof this.rowFilter === "function") {
        filtered = filtered.filter(this.rowFilter);
      } else {
        // Delegate string expression evaluation to table's select
        filtered = this.table.select(this.rowFilter).filter((r) => this.rowStateFilter.includes(r.rowState));
      }
    }

    if (this.sort) {
      filtered = this.sortTableRows(filtered, this.sort);
    }

    return filtered;
  }

  toObjects(): Record<string, unknown>[] {
    return this.toRows().map((r) => r.toObject());
  }

  get(index: number): DataRow | undefined {
    return this.toRows()[index];
  }

  find(keyOrValues: unknown | unknown[]): number {
    const rows = this.toRows();
    const keys = Array.isArray(keyOrValues) ? keyOrValues : [keyOrValues];
    const sortCols = this.getSortColumns();

    if (sortCols.length === 0) {
      throw new Error("Cannot call DataView.find() without a sort expression specified.");
    }

    for (let i = 0; i < rows.length; i++) {
      const row = rows[i];
      if (!row) continue;
      let match = true;
      for (let j = 0; j < keys.length; j++) {
        const col = sortCols[j];
        if (col && row.item(col) !== keys[j]) {
          match = false;
          break;
        }
      }
      if (match) {
        return i;
      }
    }
    return -1;
  }

  findRows(keyOrValues: unknown | unknown[]): DataRow[] {
    const rows = this.toRows();
    const keys = Array.isArray(keyOrValues) ? keyOrValues : [keyOrValues];
    const sortCols = this.getSortColumns();

    if (sortCols.length === 0) {
      throw new Error("Cannot call DataView.findRows() without a sort expression specified.");
    }

    return rows.filter((row) => {
      for (let j = 0; j < keys.length; j++) {
        const col = sortCols[j];
        if (col && row.item(col) !== keys[j]) {
          return false;
        }
      }
      return true;
    });
  }

  toTable(tableName = "ViewTable", distinct = false, columnNames?: string[]): DataTable {
    const newTable = new DataTable(tableName);
    const colsToInclude =
      columnNames && columnNames.length > 0
        ? columnNames.map((name) => {
            const col = this.table.columns.get(name);
            if (!col) throw new Error(`Column '${name}' not found in source table.`);
            return col;
          })
        : this.table.columns.toArray();

    for (const col of colsToInclude) {
      newTable.columns.add(new DataColumn(col.columnName, col.dataType));
    }

    const seen = new Set<string>();
    for (const row of this.toRows()) {
      const obj: Record<string, unknown> = {};
      for (const col of colsToInclude) {
        obj[col.columnName] = row.item(col.columnName);
      }

      if (distinct) {
        const key = JSON.stringify(obj);
        if (seen.has(key)) {
          continue;
        }
        seen.add(key);
      }

      const newRow = newTable.newRow();
      newRow.setValues(obj);
      newTable.rows.add(newRow);
      newRow.acceptChanges();
    }

    return newTable;
  }

  [Symbol.iterator](): Iterator<DataRow> {
    return this.toRows()[Symbol.iterator]();
  }

  private getSortColumns(): string[] {
    if (!this.sort) return [];
    return this.sort.split(",").map((s) => s.trim().split(/\s+/)[0] ?? "");
  }

  private sortTableRows(rows: DataRow[], sortExpression: string): DataRow[] {
    const clauses = sortExpression.split(",").map((c) => {
      const parts = c.trim().split(/\s+/);
      const colName = parts[0] ?? "";
      const dir = (parts[1]?.toUpperCase() ?? "ASC") as "ASC" | "DESC";
      return { colName, dir };
    });

    return [...rows].sort((a, b) => {
      for (const clause of clauses) {
        const valA = a.item(clause.colName);
        const valB = b.item(clause.colName);

        if (valA === valB) continue;
        if (valA === null || valA === undefined) return clause.dir === "ASC" ? -1 : 1;
        if (valB === null || valB === undefined) return clause.dir === "ASC" ? 1 : -1;

        let cmp: number;
        if (typeof valA === "number" && typeof valB === "number") {
          cmp = valA - valB;
        } else if (valA instanceof Date && valB instanceof Date) {
          cmp = valA.getTime() - valB.getTime();
        } else {
          cmp = String(valA).localeCompare(String(valB));
        }

        if (cmp !== 0) {
          return clause.dir === "ASC" ? cmp : -cmp;
        }
      }
      return 0;
    });
  }
}

