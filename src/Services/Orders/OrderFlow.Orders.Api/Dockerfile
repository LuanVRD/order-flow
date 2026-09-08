# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy project files for layer caching
COPY ["src/BuildingBlocks/OrderFlow.Messaging.Contracts/OrderFlow.Messaging.Contracts.csproj", "src/BuildingBlocks/OrderFlow.Messaging.Contracts/"]
COPY ["src/Services/Orders/OrderFlow.Orders.Domain/OrderFlow.Orders.Domain.csproj", "src/Services/Orders/OrderFlow.Orders.Domain/"]
COPY ["src/Services/Orders/OrderFlow.Orders.Application/OrderFlow.Orders.Application.csproj", "src/Services/Orders/OrderFlow.Orders.Application/"]
COPY ["src/Services/Orders/OrderFlow.Orders.Infrastructure/OrderFlow.Orders.Infrastructure.csproj", "src/Services/Orders/OrderFlow.Orders.Infrastructure/"]
COPY ["src/Services/Orders/OrderFlow.Orders.Api/OrderFlow.Orders.Api.csproj", "src/Services/Orders/OrderFlow.Orders.Api/"]

RUN dotnet restore "src/Services/Orders/OrderFlow.Orders.Api/OrderFlow.Orders.Api.csproj"

# Copy source code
COPY ["src/BuildingBlocks/OrderFlow.Messaging.Contracts/", "src/BuildingBlocks/OrderFlow.Messaging.Contracts/"]
COPY ["src/Services/Orders/", "src/Services/Orders/"]

WORKDIR "/src/src/Services/Orders/OrderFlow.Orders.Api"
RUN dotnet publish "OrderFlow.Orders.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Lean Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OrderFlow.Orders.Api.dll"]
