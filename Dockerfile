FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ExpenseTracker.sln ./
COPY ExpenseTracker.API/ExpenseTracker.API.csproj ExpenseTracker.API/
COPY ExpenseTracker.Application/ExpenseTracker.Application.csproj ExpenseTracker.Application/
COPY ExpenseTracker.Domain/ExpenseTracker.Domain.csproj ExpenseTracker.Domain/
COPY ExpenseTracker.Infrastructure/ExpenseTracker.Infrastructure.csproj ExpenseTracker.Infrastructure/
COPY ExpenseTracker.Shared/ExpenseTracker.Shared.csproj ExpenseTracker.Shared/

RUN dotnet restore ExpenseTracker.API/ExpenseTracker.API.csproj

COPY . ./
RUN dotnet publish ExpenseTracker.API/ExpenseTracker.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ExpenseTracker.API.dll"]