#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["./ScrawlDataIntraday.csproj", "ScrawlDataIntraday/"]
RUN dotnet restore "./ScrawlDataIntraday/./ScrawlDataIntraday.csproj"

WORKDIR "/src/ScrawlDataIntraday"
COPY . .
RUN dotnet build "./ScrawlDataIntraday.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ScrawlDataIntraday.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

USER root
RUN apt-get update \
  && apt-get upgrade -y \
  && apt-get install -y \
     file \
     lldb \
  && rm -rf /var/lib/apt/lists/*

ENV TZ=Asia/Ho_Chi_Minh
RUN echo $TZ > /etc/timezone && \
    apt-get update && apt-get install -y tzdata && \
    rm /etc/localtime && \
    ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && \
    dpkg-reconfigure -f noninteractive tzdata && \
    apt-get clean

RUN apt-get install -y \
    gnupg \
    wget \
    unzip \
    libglib2.0-0 \
    libnss3 \
    libgconf-2-4 \
    libfontconfig1 \
    libxrender1 \
    libxtst6 \
    x11-xkb-utils \
    xfonts-100dpi \
    xfonts-75dpi \
    xfonts-scalable \
    libappindicator3-1 \
    libasound2 \
    && rm -rf /var/lib/apt/lists/*

# Download and install Chrome
RUN mkdir -p /opt/google/chrome && chmod 755 /opt/google/chrome

# Download and extract Chrome using environment variables
RUN wget -q -O /tmp/chrome-linux64.zip https://storage.googleapis.com/chrome-for-testing-public/126.0.6478.126/linux64/chrome-linux64.zip 
    && unzip /tmp/chrome-linux64.zip -d /opt/google/chrome 
    && rm /tmp/chrome-linux64.zip

# Set up Chrome in the system PATH
RUN ln -s /opt/google/chrome /usr/bin/google-chrome


# Download and install ChromeDriver
RUN wget -O /tmp/chromedriver.zip https://storage.googleapis.com/chrome-for-testing-public/126.0.6478.126/linux64/chromedriver-linux64.zip \
    && unzip /tmp/chromedriver.zip -d /usr/local/bin/ \
    && rm /tmp/chromedriver.zip

# Set environment variables for Chrome and ChromeDriver
ENV PATH="/usr/local/bin:${PATH}"
ENV CHROME_DRIVER="/usr/local/bin/chromedriver"

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ScrawlDataIntraday.dll"]