using System.Data;
using System.Diagnostics;
using System.IO;
using System.Security;
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
            if (package.Workbook.Worksheets.Count == 0)
                return null;

            var worksheet = package.Workbook.Worksheets[0];
            if (worksheet.Dimension == null)
                return null;

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

            // 识别头像列索引（0-based）
            int avatarColIndex = -1;
            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                if (dataTable.Columns[i].ColumnName == "主播头像")
                {
                    avatarColIndex = i;
                    break;
                }
            }

            const int avatarPixelSize = 50;

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
                if (avatarColIndex >= 0)
                    worksheet.Row(row + 2).Height = avatarPixelSize * 0.75; // Excel row height is in points (1pt ≈ 1.33px)

                for (int col = 0; col < dataTable.Columns.Count; col++)
                {
                    var cell = worksheet.Cells[row + 2, col + 1];

                    if (col == avatarColIndex)
                    {
                        var base64 = dataTable.Rows[row][col]?.ToString();
                        if (!string.IsNullOrEmpty(base64))
                        {
                            try
                            {
                                var imgBytes = Convert.FromBase64String(base64);
                                using var ms = new MemoryStream(imgBytes);
                                var picture = worksheet.Drawings.AddPicture(
                                    $"avatar_{row}", ms);
                                picture.SetPosition(row + 1, 2, col, 2);
                                picture.SetSize(avatarPixelSize - 4, avatarPixelSize - 4);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Excel图片嵌入失败(行{row}): {ex.Message}");
                                cell.Value = "图片加载失败";
                            }
                        }
                        else
                        {
                            cell.Value = "无头像";
                        }
                    }
                    else
                    {
                        cell.Value = dataTable.Rows[row][col];
                    }

                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                }
            }

            // 自动调整列宽
            worksheet.Cells.AutoFitColumns();

            // 头像列设置固定宽度（9个字符宽，约可容纳50px头像图片）
            if (avatarColIndex >= 0)
                worksheet.Column(avatarColIndex + 1).Width = 9;

            // 冻结首行
            worksheet.View.FreezePanes(2, 1);

            package.SaveAs(new FileInfo(filePath));
        });
    }

    public async Task<bool> CheckFileWritableAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var directoryPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return false;

            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.IsReadOnly)
                    return false;

                await using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            else
            {
                var tempFilePath = Path.Combine(directoryPath, $".{Guid.NewGuid():N}.tmp");
                await using var stream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}