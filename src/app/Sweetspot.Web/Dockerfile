# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY .build/publish/ .
#COPY ./.build/Sweetspot.Web/publish/ .
ENTRYPOINT [ "dotnet", "Sweetspot.Web.dll" ]