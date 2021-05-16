# ** Build

FROM node:12 as build
WORKDIR /app

RUN npm install --global yarn

COPY src/NuGetTrends.Web/Portal/package.json .
COPY src/NuGetTrends.Web/Portal/yarn.lock .

RUN yarn install

COPY src/NuGetTrends.Web/Portal/tsconfig.json .
COPY src/NuGetTrends.Web/Portal/tsconfig.app.json .
COPY src/NuGetTrends.Web/Portal/tsconfig.spec.json .
COPY src/NuGetTrends.Web/Portal/tslint.json .
COPY src/NuGetTrends.Web/Portal/angular.json .
COPY src/NuGetTrends.Web/Portal/e2e/ e2e/
COPY src/NuGetTrends.Web/Portal/src/ src/

RUN npm run build

# ** Run

FROM nginx:1.16.0 as run

EXPOSE 80
EXPOSE 443

RUN rm /etc/nginx/conf.d/default.conf
COPY src/NuGetTrends.Web/Portal/nginx/nginx.conf /etc/nginx/conf.d

COPY --from=build /app/dist /usr/share/nginx/html

ENTRYPOINT ["nginx", "-g", "daemon off;"]
