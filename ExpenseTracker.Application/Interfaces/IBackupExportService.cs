using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpenseTracker.Application.Interfaces
{
    public interface IBackupExportService
    {
        Task WriteExportZipAsync(Stream output, CancellationToken cancellationToken);
    }
}
