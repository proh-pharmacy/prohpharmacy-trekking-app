FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY *.csproj .
RUN dotnet restore

COPY . .
RUN touch .env

RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=build /src/.env .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

RUN printf '#!/bin/bash\nset -a\n[ -f /app/.env ] && . /app/.env\nset +a\nexec dotnet prohpharmacy_trekking_app.dll\n' > /app/entrypoint.sh && chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]
