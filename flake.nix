{
  description = "NuGet Trends development environment";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs {
          inherit system;
        };
      in
      {
        devShells.default = pkgs.mkShell {
          buildInputs = with pkgs; [
            # .NET SDK 10
            dotnet-sdk_10

            # Docker tools
            docker
            docker-compose

            # Node.js and NPM
            nodejs_20

            # Useful tools
            git
          ];

          shellHook = ''
            echo "🚀 NuGet Trends development environment"
            echo ""
            echo "Available tools:"
            echo "  dotnet: $(dotnet --version)"
            echo "  node: $(node --version)"
            echo "  npm: $(npm --version)"
            echo "  docker: $(docker --version 2>/dev/null || echo 'not running (needs system daemon)')"
            echo "  docker-compose: $(docker-compose --version)"
            echo ""
            echo "To start the project:"
            echo "  dotnet run --project src/NuGetTrends.AppHost"
            echo ""

            # Set DOTNET_ROOT to help tools find the SDK
            export DOTNET_ROOT="${pkgs.dotnet-sdk}"

            # Enable .NET telemetry opt-out (optional, for privacy)
            export DOTNET_CLI_TELEMETRY_OPTOUT=1

            # Set Node environment
            export NODE_ENV=development
          '';
        };
      }
    );
}
