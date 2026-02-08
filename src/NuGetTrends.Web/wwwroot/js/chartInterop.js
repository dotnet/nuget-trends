// Chart.js interop for Blazor
window.chartInterop = {
    chart: null,

    init: function (canvasId, config) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            console.error('Canvas not found:', canvasId);
            return;
        }

        // Destroy existing chart if any
        if (this.chart) {
            this.chart.destroy();
        }

        const ctx = canvas.getContext('2d');
        const colors = this.getThemeColors();

        // Configure chart with theme colors
        Chart.defaults.font.size = 13;
        Chart.defaults.color = colors.text;

        // Merge options with theme-aware settings
        const options = config.options || {};
        options.plugins = options.plugins || {};
        options.plugins.tooltip = {
            animation: false,
            backgroundColor: colors.tooltipBg,
            titleColor: colors.tooltipText,
            bodyColor: colors.tooltipText,
            callbacks: {
                title: function (tooltipItems) {
                    const rawData = tooltipItems[0].raw;
                    const date = new Date(rawData.x);
                    return date.toLocaleDateString('en-US', { day: '2-digit', month: 'short', year: 'numeric' });
                },
                label: function (tooltipItem) {
                    let label = tooltipItem.dataset.label || 'NuGet Package';
                    const rawData = tooltipItem.raw;
                    return label + ': ' + rawData.y.toLocaleString();
                }
            }
        };

        options.scales = options.scales || {};
        options.scales.x = options.scales.x || {};
        options.scales.x.grid = { color: colors.grid };
        options.scales.x.ticks = { color: colors.text };

        options.scales.y = options.scales.y || {};
        options.scales.y.grid = { color: colors.grid };
        options.scales.y.ticks = {
            color: colors.text,
            callback: function (value) {
                return value.toLocaleString();
            }
        };

        // Parse date strings in datasets
        if (config.data && config.data.datasets) {
            config.data.datasets.forEach(ds => {
                if (ds.data) {
                    ds.data = ds.data.map(d => ({
                        x: new Date(d.x).getTime(),
                        y: d.y
                    }));
                }
            });
        }

        config.options = options;
        this.chart = new Chart(ctx, config);
    },

    addDataset: function (dataset) {
        if (!this.chart) {
            console.error('Chart not initialized');
            return;
        }

        // Parse date strings
        if (dataset.data) {
            dataset.data = dataset.data.map(d => ({
                x: new Date(d.x).getTime(),
                y: d.y
            }));
        }

        // Replace existing dataset with same label, or add new one
        const existingIndex = this.chart.data.datasets.findIndex(ds => ds.label === dataset.label);
        if (existingIndex >= 0) {
            this.chart.data.datasets[existingIndex] = dataset;
        } else {
            this.chart.data.datasets.push(dataset);
        }
        this.safeUpdate();
    },

    removeDataset: function (label) {
        if (!this.chart) {
            return;
        }

        const index = this.chart.data.datasets.findIndex(ds => ds.label === label);
        if (index >= 0) {
            this.chart.data.datasets.splice(index, 1);
            this.safeUpdate();
        }
    },

    updateTheme: function (isDark) {
        if (!this.chart) {
            return;
        }

        const colors = this.getThemeColors();

        Chart.defaults.color = colors.text;

        if (this.chart.options.scales) {
            const xScale = this.chart.options.scales.x;
            const yScale = this.chart.options.scales.y;

            if (xScale) {
                if (xScale.grid) xScale.grid.color = colors.grid;
                if (xScale.ticks) xScale.ticks.color = colors.text;
            }
            if (yScale) {
                if (yScale.grid) yScale.grid.color = colors.grid;
                if (yScale.ticks) yScale.ticks.color = colors.text;
            }
        }

        if (this.chart.options.plugins && this.chart.options.plugins.tooltip) {
            this.chart.options.plugins.tooltip.backgroundColor = colors.tooltipBg;
            this.chart.options.plugins.tooltip.titleColor = colors.tooltipText;
            this.chart.options.plugins.tooltip.bodyColor = colors.tooltipText;
        }

        this.safeUpdate();
    },

    safeUpdate: function () {
        if (this.chart && this.chart.canvas) {
            this.chart.setActiveElements([]);
            this.chart.update();
        }
    },

    destroy: function () {
        if (this.chart) {
            this.chart.destroy();
            this.chart = null;
        }
    },

    getThemeColors: function () {
        const style = getComputedStyle(document.body);
        return {
            grid: style.getPropertyValue('--chart-grid-color').trim() || 'rgba(0, 0, 0, 0.1)',
            text: style.getPropertyValue('--chart-text-color').trim() || '#666666',
            tooltipBg: style.getPropertyValue('--chart-tooltip-bg').trim() || 'rgba(0, 0, 0, 0.8)',
            tooltipText: style.getPropertyValue('--chart-tooltip-text').trim() || '#ffffff'
        };
    }
};
