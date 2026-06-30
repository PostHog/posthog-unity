FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY sdk_compliance_adapter/PostHog.Unity.ComplianceAdapter.csproj sdk_compliance_adapter/
RUN dotnet restore sdk_compliance_adapter/PostHog.Unity.ComplianceAdapter.csproj
COPY sdk_compliance_adapter/ sdk_compliance_adapter/
RUN dotnet publish sdk_compliance_adapter/PostHog.Unity.ComplianceAdapter.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PostHog.Unity.ComplianceAdapter.dll"]
