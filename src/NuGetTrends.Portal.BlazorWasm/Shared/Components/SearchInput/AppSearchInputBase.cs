using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Timers;

namespace NuGetTrends.Portal.BlazorWasm.Shared.Components.SearchInput
{
    public class AppSearchInputBase : ComponentBase
    {
        private EditContext _editContext;
        private FieldIdentifier _fieldIdentifier;
        private Timer _debounceTimer;
        private string _searchText = string.Empty;
        private bool _eventsHookedUp = false;
        private bool _resettingControl = false;
        private bool _isShowingSuggestions = false;
        private int _selectedIndex { get; set; }
        private bool IsSearchingOrDebouncing => IsSearching || _debounceTimer.Enabled;

        protected ElementReference SearchInput;

        [Inject] private IJSRuntime JSRuntime { get; set; }
        [Parameter] public Package Value { get; set; }
        [CascadingParameter] private EditContext CascadedEditContext { get; set; }
        [Parameter] public EventCallback<Package> ValueChanged { get; set; }
        [Parameter] public Expression<Func<Package>> ValueExpression { get; set; }
        [Parameter] public Func<string, Task<IEnumerable<Package>>> SearchMethod { get; set; }
        [Parameter] public int MinimumLength { get; set; } = 3;
        [Parameter] public int Debounce { get; set; } = 300;
        [Parameter] public int MaximumSuggestions { get; set; } = 10;
        [Parameter] public bool StopPropagation { get; set; } = false;
        [Parameter] public bool PreventDefault { get; set; } = false;

        protected bool IsSearching = false;

        protected Package[] Suggestions { get; set; } = new Package[0];
        
        protected string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;

                if (value.Length == 0)
                {
                    _debounceTimer.Stop();
                    _selectedIndex = -1;
                }
                else
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
            }
        }

        protected override void OnInitialized()
        {
            if (SearchMethod == null)
            {
                throw new InvalidOperationException($"{GetType()} requires a {nameof(SearchMethod)} parameter.");
            }

            _debounceTimer = new Timer();
            _debounceTimer.Interval = Debounce;
            _debounceTimer.AutoReset = false;
            _debounceTimer.Elapsed += Search;

            _editContext = CascadedEditContext;
            _fieldIdentifier = FieldIdentifier.Create(ValueExpression);

            Initialize();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender || (!_eventsHookedUp))
            {
                await AddKeyDownEventListener(JSRuntime, SearchInput);
                _eventsHookedUp = true;
                await Focus(JSRuntime, SearchInput);
            }
        }

        protected override void OnParametersSet()
        {
            Initialize();
        }

        protected async Task HandleKeyup(KeyboardEventArgs args)
        {
            if ((args.Key == "ArrowDown" || args.Key == "Enter") && !_isShowingSuggestions)
            {
                await ShowMaximumSuggestions();
            }

            if (args.Key == "ArrowDown")
            {
                MoveSelection(1);
            }
            else if (args.Key == "ArrowUp")
            {
                MoveSelection(-1);
            }
            else if (args.Key == "Escape")
            {
                Initialize();
            }
            else if (args.Key == "Enter" && Suggestions.Count() == 1)
            {
                await SelectTheFirstAndOnlySuggestion();
            }
            else if (args.Key == "Enter" && _selectedIndex >= 0 && _selectedIndex < Suggestions.Count())
            {
                await SelectResult(Suggestions[_selectedIndex]);
            }
        }

        protected async Task SelectResult(Package item)
        {
            await ValueChanged.InvokeAsync(item);

            _editContext?.NotifyFieldChanged(_fieldIdentifier);

            SearchText = string.Empty;

            await Task.Delay(250); // Possible race condition here.
            await Focus(JSRuntime, SearchInput);
        }

        protected bool ShouldShowSuggestions()
        {
            return _isShowingSuggestions &&
                   Suggestions.Any() && !IsSearchingOrDebouncing;
        }

        protected async Task ResetControl()
        {
            if (!_resettingControl)
            {
                _resettingControl = true;
                await Task.Delay(200);
                Initialize();
                _resettingControl = false;
            }
        }

        protected bool ShowNotFound()
        {
            return _isShowingSuggestions &&
                   !IsSearchingOrDebouncing &&
                   !Suggestions.Any();
        }

        private void Initialize()
        {
            SearchText = "";
            _isShowingSuggestions = false;
        }

        private async Task ShowMaximumSuggestions()
        {
            if (_resettingControl)
            {
                while (_resettingControl)
                {
                    await Task.Delay(150);
                }
            }

            _isShowingSuggestions = !_isShowingSuggestions;

            if (_isShowingSuggestions)
            {
                SearchText = "";
                IsSearching = true;
                await InvokeAsync(StateHasChanged);

                Suggestions = (await SearchMethod?.Invoke(_searchText)).Take(MaximumSuggestions).ToArray();

                IsSearching = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async void Search(object source, ElapsedEventArgs e)
        {
            if (_searchText.Length < MinimumLength)
            {
                await InvokeAsync(StateHasChanged);
                return;
            }

            IsSearching = true;
            await InvokeAsync(StateHasChanged);
            Suggestions = (await SearchMethod?.Invoke(_searchText)).Take(MaximumSuggestions).ToArray();

            IsSearching = false;
            _isShowingSuggestions = true;
            _selectedIndex = 0;
            await InvokeAsync(StateHasChanged);
        }

        protected string GetSelectedSuggestionClass(int index)
        {
            if (index == _selectedIndex)
                return "active-item";

            return string.Empty;
        }

        private void MoveSelection(int count)
        {
            var index = _selectedIndex + count;

            if (index >= Suggestions.Length)
            {
                index = 0;
            }

            if (index < 0)
            {
                index = Suggestions.Length - 1;
            }

            _selectedIndex = index;
        }

        private Task SelectTheFirstAndOnlySuggestion()
        {
            _selectedIndex = 0;
            return SelectResult(Suggestions[_selectedIndex]);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _debounceTimer.Dispose();
            }
        }

        private static ValueTask<object> Focus(IJSRuntime jsRuntime, ElementReference element)
        {
            return jsRuntime.InvokeAsync<object>("appSearchInput.setFocus", element);
        }

        private static ValueTask<object> AddKeyDownEventListener(IJSRuntime jsRuntime, ElementReference element)
        {
            return jsRuntime.InvokeAsync<object>("appSearchInput.addKeyDownEventListener", element);
        }
    }
}
