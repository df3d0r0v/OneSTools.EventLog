FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env

USER "NT Authority\System"
WORKDIR /App

COPY . .

RUN dotnet restore

RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/runtime:6.0-nanoserver-1809

WORKDIR C:\\Program Files\\EventLogExporter\\data

COPY --from=build-env /App/out ../

USER "NT Authority\System"

ENTRYPOINT ["cmd.exe"]
#ENTRYPOINT ["dotnet", "EventLogExportersManager.dll",]