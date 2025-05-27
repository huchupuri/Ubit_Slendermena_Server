FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Копируем файл проекта
COPY *.csproj ./
RUN dotnet restore

# Копируем остальные файлы
COPY . ./
RUN dotnet publish -c Release -o out

# Создаем финальный образ
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "Ubit_Slendermena_Server.dll"]