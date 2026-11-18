# Maths 4e : API de suivi (VB.NET) + site web, dans une seule image.
# Construit par docker-compose.yml ; voir deploy/README.md.

# ---- Compilation de l'API
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS compilation
WORKDIR /src
COPY api/MathsSuivi.Api/MathsSuivi.Api.vbproj api/MathsSuivi.Api/
RUN dotnet restore api/MathsSuivi.Api/MathsSuivi.Api.vbproj
COPY api/MathsSuivi.Api/ api/MathsSuivi.Api/
RUN dotnet publish api/MathsSuivi.Api/MathsSuivi.Api.vbproj -c Release -o /publication --no-restore

# ---- Image finale
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=compilation /publication .
COPY index.html prof.html sw.js manifest.webmanifest site/
COPY icons/ site/icons/
# Le site et l'API sont servis à la même adresse
RUN printf '/* Image Docker : le site et l API sont servis a la meme adresse. */\nwindow.MATHS4E_API = location.origin;\n' > site/config.js

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    Site__Dossier=/app/site \
    DOTNET_NOLOGO=1
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "MathsSuivi.Api.dll"]
