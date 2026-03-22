using System.Data;
using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace DmPayQuery.Services;

public class ExcelService : IExcelService
{
    static ExcelService()
    {
        // EPPlus 8 新方式：使用 License 属性的方法设置
        ExcelPackage.License.SetNonCommercialPersonal("DmPayQuery User");
    }

    public async Task<DataTable?> ReadExcelAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets[0];
            var dt = new DataTable();

            // 添加列（只添加有标题的非空列）
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[1, col].Text?.Trim();
                // 跳过空列名，或者跳过关键字"Column"开头的列
                if (!string.IsNullOrWhiteSpace(header) && !header.StartsWith("Column"))
                {
                    dt.Columns.Add(header);
                }
                else if (!string.IsNullOrWhiteSpace(header))
                {
                    // 如果有Column开头的列，说明是模板问题，也添加但记录日志
                    dt.Columns.Add(header);
                }
            }

            // 添加行
            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var dataRow = dt.NewRow();
                int validColIndex = 0;

                for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                {
                    var header = worksheet.Cells[1, col].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(header))
                    {
                        if (validColIndex < dt.Columns.Count)
                        {
                            dataRow[validColIndex] = worksheet.Cells[row, col].Text;
                            validColIndex++;
                        }
                    }
                }

                // 只添加非空行
                if (dataRow.ItemArray.Any(x => !string.IsNullOrWhiteSpace(x?.ToString())))
                {
                    dt.Rows.Add(dataRow);
                }
            }

            return dt;
        });
    }

    public async Task SaveExcelAsync(DataTable dataTable, string filePath)
    {
        await Task.Run(() =>
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("查询结果");

            // 写入表头
            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = dataTable.Columns[i].ColumnName;
                worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                worksheet.Cells[1, i + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            // 写入数据
            for (int row = 0; row < dataTable.Rows.Count; row++)
            {
                for (int col = 0; col < dataTable.Columns.Count; col++)
                {
                    var cell = worksheet.Cells[row + 2, col + 1];
                    cell.Value = dataTable.Rows[row][col];
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }
            }

            // 自动调整列宽
            worksheet.Cells.AutoFitColumns();

            // 冻结首行
            worksheet.View.FreezePanes(2, 1);

            package.SaveAs(new FileInfo(filePath));
        });
    }

    public async Task<bool> CheckFileWritableAsync(string filePath)
    {
        try
        {
            await using var stream = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}