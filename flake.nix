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

        # FHS environment for running .NET Aspire AppHost
        # This is needed because Aspire's DCP binary is dynamically linked for generic Linux
        aspire-fhs = pkgs.buildFHSEnv {
          name = "aspire-env";
          targetPkgs = pkgs: with pkgs; [
            dotnet-sdk_10
            icu
            zlib
            openssl
            openssl.bin
            stdenv.cc.cc.lib
            cacert
            strace
            curl
          ];
          runScript = "bash";
          profile = ''
            export SSL_CERT_FILE=/etc/ssl/certs/ca-bundle.crt
            export GODEBUG=x509sha1=1
            export DCP_IP_VERSION_PREFERENCE=IPv4
          '';
        };
      in
      {
        devShells.default = pkgs.mkShell {
          buildInputs = with pkgs; [
            # .NET SDK 10
            dotnet-sdk_10

            # Aspire FHS wrapper
            aspire-fhs

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
            echo "  aspire-env -c 'dotnet run --project src/NuGetTrends.AppHost'"
            echo ""
            echo "Note: Aspire AppHost requires FHS environment (use aspire-env wrapper)"
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
