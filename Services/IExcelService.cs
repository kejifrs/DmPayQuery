using System.Data;

namespace DmPayQuery.Services;

public interface IExcelService
{
    Task<DataTable?> ReadExcelAsync(string filePath);
    Task SaveExcelAsync(DataTable dataTable, string filePath);
    Task<bool> CheckFileWritableAsync(string filePath);
}