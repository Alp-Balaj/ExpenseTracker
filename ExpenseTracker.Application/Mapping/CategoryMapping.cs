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
                Color = entity.Color
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
            };
        }
        public static void Apply(this Category entity, CategoryDTO dto)
        {
            entity.Name = dto.Name;
            entity.Description = dto.Description;
            entity.CategoryType = (CategoryType)dto.CategoryType;
            entity.Color = dto.Color;
        }
    }
}