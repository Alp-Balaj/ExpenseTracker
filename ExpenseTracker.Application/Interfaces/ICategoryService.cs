using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpenseTracker.Application.Interfaces;
public interface ICategoryService
{
    Task<IEnumerable<CategorySummaryDTO>> GetCategorySummariesAsync(CategoryType categoryType);
}

