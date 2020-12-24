const process = require('process');

const isBuildServer = process.env.IS_BUILD_SERVER;
process.env.CHROME_BIN = require('puppeteer').executablePath();

module.exports = function (config) {

  let _config = {
    basePath: '',
    frameworks: ['jasmine', '@angular-devkit/build-angular'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
      require('@angular-devkit/build-angular/plugins/karma')
    ],
    client: {
      clearContext: false // leave Jasmine Spec Runner output visible in browser
    },
    coverageReporter: {
      dir: require('path').join(__dirname, '../coverage'),
      subdir: '.',
      reporters: [
        { type: 'html' },
        { type: 'lcovonly' },
        { type: 'text-summary' },
        { type: 'cobertura' }
      ],
    },
    reporters: ['progress', 'kjhtml'],
    port: 9876,
    colors: true,
    logLevel: config.LOG_INFO,
    autoWatch: true,
    browsers: [],
    singleRun: false
  }

  if (isBuildServer) {
    _config.browsers.push('ChromeHeadless');
    _config.plugins.push(require('karma-junit-reporter'));
    _config.reporters.push('junit');
  } else {
    _config.browsers.push('Chrome');
  }

  config.set(_config);
};
