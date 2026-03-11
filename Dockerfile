FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["BookingGuardian/BookingGuardian.csproj", "BookingGuardian/"]
RUN dotnet restore "BookingGuardian/BookingGuardian.csproj"

# Copy everything else and build
COPY BookingGuardian/ BookingGuardian/
WORKDIR "/src/BookingGuardian"
RUN dotnet build "BookingGuardian.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BookingGuardian.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BookingGuardian.dll"]
