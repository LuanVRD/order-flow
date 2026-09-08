# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy project files for layer caching
COPY ["src/BuildingBlocks/OrderFlow.Messaging.Contracts/OrderFlow.Messaging.Contracts.csproj", "src/BuildingBlocks/OrderFlow.Messaging.Contracts/"]
COPY ["src/Services/Notifications/OrderFlow.Notifications.Domain/OrderFlow.Notifications.Domain.csproj", "src/Services/Notifications/OrderFlow.Notifications.Domain/"]
COPY ["src/Services/Notifications/OrderFlow.Notifications.Application/OrderFlow.Notifications.Application.csproj", "src/Services/Notifications/OrderFlow.Notifications.Application/"]
COPY ["src/Services/Notifications/OrderFlow.Notifications.Infrastructure/OrderFlow.Notifications.Infrastructure.csproj", "src/Services/Notifications/OrderFlow.Notifications.Infrastructure/"]
COPY ["src/Services/Notifications/OrderFlow.Notifications.Worker/OrderFlow.Notifications.Worker.csproj", "src/Services/Notifications/OrderFlow.Notifications.Worker/"]

RUN dotnet restore "src/Services/Notifications/OrderFlow.Notifications.Worker/OrderFlow.Notifications.Worker.csproj"

# Copy source code
COPY ["src/BuildingBlocks/OrderFlow.Messaging.Contracts/", "src/BuildingBlocks/OrderFlow.Messaging.Contracts/"]
COPY ["src/Services/Notifications/", "src/Services/Notifications/"]

WORKDIR "/src/src/Services/Notifications/OrderFlow.Notifications.Worker"
RUN dotnet publish "OrderFlow.Notifications.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Lean Runtime
FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine AS final
WORKDIR /app

RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

USER $APP_UID

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OrderFlow.Notifications.Worker.dll"]
