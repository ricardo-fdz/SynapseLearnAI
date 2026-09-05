# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LearningAgents.slnx ./
COPY src/LearningAgents.Domain/ src/LearningAgents.Domain/
COPY src/LearningAgents.Infrastructure/ src/LearningAgents.Infrastructure/
COPY src/LearningAgents.Application/ src/LearningAgents.Application/
COPY src/LearningAgents.Api/ src/LearningAgents.Api/
RUN dotnet publish src/LearningAgents.Api/LearningAgents.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# SQLite DB persisted via volume
VOLUME ["/app/data"]
ENV ASPNETCORE_URLS=http://+:5017
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5017
ENTRYPOINT ["dotnet", "LearningAgents.Api.dll"]
