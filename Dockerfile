FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Amadban.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish --no-restore -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --chown=app:app --from=build /publish .
RUN mkdir -p /keys && chown app:app /keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Amadban.dll"]
