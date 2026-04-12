# syntax=docker/dockerfile:1

# ---------- Stage 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj first, restore — gives us layer caching so changing
# source doesn't re-download all NuGet packages.
COPY FinanceManagement.API.csproj ./
RUN dotnet restore

# Now copy the rest and publish
COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The API listens on 8080 inside the container
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "FinanceManagement.API.dll"]
