const { codecovWebpackPlugin } = require("@codecov/webpack-plugin");

module.exports = {
  plugins: [
    codecovWebpackPlugin({
      enableBundleAnalysis: process.env.CODECOV_TOKEN !== undefined,
      bundleName: "nuget-trends-spa",
      dryRun: true,
      uploadToken: process.env.CODECOV_TOKEN,
    }),
  ]
};
