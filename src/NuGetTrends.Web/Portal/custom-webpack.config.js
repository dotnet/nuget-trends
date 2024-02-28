const { codecovWebpackPlugin } = require("@codecov/webpack-plugin");

module.exports = {
  plugins: [
    codecovWebpackPlugin({
      enableBundleAnalysis: process.env.NODE_ENV === "production",
      bundleName: "nuget-trends-spa",
      uploadToken: process.env.CODECOV_TOKEN,
    }),
  ]
};
