# FluentTransformer

This is a basic version of my transformer application built using the [FluentUI](https://www.fluentui-blazor.net/) component library.

## Usage

### Visual Studio

This app can be run locally by just cloning using visual studio and then running it.

### Steps to run using Docker

You can also pull and run it in a docker container and it is hosted on dockerhub at mbishop84/fluenttransformer.

1. `docker pull mbishop84/fluenttransformer`
2. `docker run --rm -d -P -e "ASPNETCORE_ENVIRONMENT=Development" --name Transformer mbishop84/fluenttransformer`
3. use `docker port Transformer` or `docker ps -a` to see the ports it is mapped to and you can navigate to that localhost port in any browser.

### Build it locally and run in a container with docker desktop

1. From the root folder build the docker image with:
   - `docker build --pull -t "fluent-transformer:local" "./FluentTransformer"`
2. Run a container from that image with:
   - `docker run --rm -d -P -e "ASPNETCORE_ENVIRONMENT=Development" --name Transformer fluent-transformer:local`
