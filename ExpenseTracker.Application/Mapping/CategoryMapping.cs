using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpenseTracker.Application.Mapping
{
    public static class CategoryMapping
    {
        public static CategoryDTO ToDto(this Category entity)
        {
            return new CategoryDTO
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                CategoryType = (int)entity.CategoryType,
                Color = entity.Color,
                // Cycle U W2. Out only: see the three notes below.
                SystemKind = entity.SystemKind
            };
        }
        public static Category ToEntity(this CategoryDTO dto)
        {
            return new Category
            {
                UserId = null!,

                Name = dto.Name,
                Description = dto.Description,
                CategoryType = (CategoryType)dto.CategoryType,
                Color = dto.Color
                // SystemKind is deliberately absent. It is the application's
                // mark on its own row, not a field a client owns — a POST that
                // set it could mint an undeletable category, which is a small
                // thing to be able to do and an annoying one to undo.
            };
        }
        public static void Apply(this Category entity, CategoryDTO dto)
        {
            entity.Name = dto.Name;
            entity.Description = dto.Description;
            entity.CategoryType = (CategoryType)dto.CategoryType;
            entity.Color = dto.Color;
            // SystemKind is not applied, so an update cannot clear the mark and
            // walk past the delete guard. Renaming and recolouring the "Shared"
            // category stay available — it lives in the user's own taxonomy, and
            // the only thing reserved about it is that it cannot be removed
            // while their pot spending still points at it.
        }
    }
}