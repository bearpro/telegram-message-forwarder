FROM mcr.microsoft.com/dotnet/sdk:7.0 as build
WORKDIR /build
COPY /TelegramMessageForwarder.Bot .
RUN dotnet build --configuration Release

FROM mcr.microsoft.com/dotnet/runtime:7.0
WORKDIR /app
COPY --from=build /build/bin/Release/net7.0 .
COPY /secrets/WTelegram.session .
ENTRYPOINT dotnet TelegramMessageForwarder.Bot.dll
