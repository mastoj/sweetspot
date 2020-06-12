# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY .build/publish/ .
#COPY ./app/Sweetspot.CSharpWorker/.build/Sweetspot.CSharpWorker/publish/ .
ENTRYPOINT [ "dotnet", "Sweetspot.CSharpWorker.dll" ]