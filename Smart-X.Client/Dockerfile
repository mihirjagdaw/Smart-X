FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Smart-X.Shared/Smart-X.Shared.csproj Smart-X.Shared/
COPY Smart-X.Client/Smart-X.Client.csproj Smart-X.Client/
RUN dotnet restore Smart-X.Client/Smart-X.Client.csproj

COPY Smart-X.Shared/ Smart-X.Shared/
COPY Smart-X.Client/ Smart-X.Client/
RUN dotnet publish Smart-X.Client/Smart-X.Client.csproj -c Release -o /app/publish

FROM nginx:alpine AS runtime
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY Smart-X.Client/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
