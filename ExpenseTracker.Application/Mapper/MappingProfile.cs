using AutoMapper;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Mapping
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Expense, ExpenseDTO>().ReverseMap();
            CreateMap<ExpenseDTO, Expense>().ReverseMap();

            CreateMap<Income, IncomeDTO>().ReverseMap();
            CreateMap<IncomeDTO, Income>().ReverseMap();
        }
    }
}
