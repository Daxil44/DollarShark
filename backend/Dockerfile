# DollarShark API image.
# Stage 1 builds with the full .NET SDK; stage 2 runs on the much smaller ASP.NET runtime image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project files first so "dotnet restore" is cached until dependencies change.
COPY DollarShark.Domain/DollarShark.Domain.csproj DollarShark.Domain/
COPY DollarShark.Application/DollarShark.Application.csproj DollarShark.Application/
COPY DollarShark.Infrastructure/DollarShark.Infrastructure.csproj DollarShark.Infrastructure/
COPY DollarShark.Api/DollarShark.Api.csproj DollarShark.Api/
RUN dotnet restore DollarShark.Api/DollarShark.Api.csproj

COPY . .
RUN dotnet publish DollarShark.Api/DollarShark.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Folders for volumes (login keys, statement files), owned by the image's built-in non-root user.
RUN mkdir -p /app/keys /app/statements && chown -R $APP_UID /app/keys /app/statements
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
ENTRYPOINT ["dotnet", "DollarShark.Api.dll"]
