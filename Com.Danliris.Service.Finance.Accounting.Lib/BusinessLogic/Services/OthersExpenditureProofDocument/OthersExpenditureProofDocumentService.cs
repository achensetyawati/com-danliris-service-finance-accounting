using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Com.Danliris.Service.Finance.Accounting.Lib.BusinessLogic.Services.DailyBankTransaction;
using Com.Danliris.Service.Finance.Accounting.Lib.BusinessLogic.Services.JournalTransaction;
using Com.Danliris.Service.Finance.Accounting.Lib.Models.OthersExpenditureProofDocument;
using Com.Danliris.Service.Finance.Accounting.Lib.Services.HttpClientService;
using Com.Danliris.Service.Finance.Accounting.Lib.Services.IdentityService;
using Com.Danliris.Service.Finance.Accounting.Lib.Utilities;
using Com.Danliris.Service.Finance.Accounting.Lib.ViewModels.NewIntegrationViewModel;
using Com.Danliris.Service.Finance.Accounting.Lib.ViewModels.OthersExpenditureProofDocumentViewModels;
using Com.Moonlay.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Com.Danliris.Service.Finance.Accounting.Lib.Services.OthersExpenditureProofDocument
{
    public class OthersExpenditureProofDocumentService : IOthersExpenditureProofDocumentService
    {
        private const string _userAgent = "finance-accounting-service";
        private const int _taskDone = 1;

        private readonly FinanceDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;
        private readonly IIdentityService _identityService;
        private readonly IAutoJournalService _autoJournalService;
        private readonly IAutoDailyBankTransactionService _autoDailyBankTransactionService;
        private readonly DbSet<OthersExpenditureProofDocumentModel> _dbSet;
        private readonly DbSet<OthersExpenditureProofDocumentItemModel> _itemDbSet;

        public OthersExpenditureProofDocumentService(FinanceDbContext dbContext, IServiceProvider serviceProvider)
        {
            _dbContext = dbContext;
            _dbSet = dbContext.Set<OthersExpenditureProofDocumentModel>();
            _itemDbSet = dbContext.Set<OthersExpenditureProofDocumentItemModel>();

            _serviceProvider = serviceProvider;
            _identityService = serviceProvider.GetService<IIdentityService>();
            _autoJournalService = serviceProvider.GetService<IAutoJournalService>();
            _autoDailyBankTransactionService = serviceProvider.GetService<IAutoDailyBankTransactionService>();
        }

        public async Task<int> CreateAsync(OthersExpenditureProofDocumentCreateUpdateViewModel viewModel)
        {
            var model = viewModel.MapToModel();
            model.DocumentNo = await GetDocumentNo("K", viewModel.AccountBankCode, _identityService.Username);
            EntityExtension.FlagForCreate(model, _identityService.Username, _userAgent);
            _dbSet.Add(model);
            await _dbContext.SaveChangesAsync();

            var itemModels = viewModel.MapItemToModel().Select(item =>
            {
                EntityExtension.FlagForCreate(item, _identityService.Username, _userAgent);
                item.OthersExpenditureProofDocumentId = model.Id;
                return item;
            }).ToList();
            _itemDbSet.UpdateRange(itemModels);
            await _dbContext.SaveChangesAsync();

            //await _autoJournalService.AutoJournalFromOthersExpenditureProof(viewModel, model.DocumentNo);
            //await _autoDailyBankTransactionService.AutoCreateFromOthersExpenditureProofDocument(model, itemModels);

            return _taskDone;
        }

        private async Task<string> GetDocumentNo(string type, string bankCode, string username)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            var http = _serviceProvider.GetService<IHttpClientService>();
            var uri = APIEndpoint.Purchasing + $"bank-expenditure-notes/bank-document-no?type={type}&bankCode={bankCode}&username={username}";
            var response = await http.GetAsync(uri);

            var result = new BaseResponse<string>();

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                result = JsonConvert.DeserializeObject<BaseResponse<string>>(responseContent, jsonSerializerSettings);
            }
            return result.data;
        }

        //private string DocumentNoGenerator(OthersExpenditureProofDocumentCreateUpdateViewModel viewModel)
        //{
        //    var latestDocumentNo = _dbSet.IgnoreQueryFilters().Where(document => document.DocumentNo.Contains(viewModel.AccountBankCode)).OrderByDescending(document => document.Id).Select(document => new { document.DocumentNo, document.CreatedUtc }).FirstOrDefault();

        //    var now = DateTimeOffset.Now;
        //    if (latestDocumentNo == null)
        //    {
        //        return $"{now.ToString("yy")}{now.ToString("MM")}{viewModel.AccountBankCode}K0001";
        //    }
        //    else
        //    {
        //        if (latestDocumentNo.CreatedUtc.Month != now.Month)
        //            return $"{now.ToString("yy")}{now.ToString("MM")}{viewModel.AccountBankCode}K0001";
        //        else
        //        {
        //            var numberString = latestDocumentNo.DocumentNo.Split("K").ToList()[1];
        //            var number = int.Parse(numberString) + 1;
        //            return $"{now.ToString("yy")}{now.ToString("MM")}{viewModel.AccountBankCode}K{number.ToString().PadLeft(4, '0')}";
        //        }
        //    }
        //}

        public async Task<int> DeleteAsync(int id)
        {
            var model = _dbSet.FirstOrDefault(document => document.Id == id);
            EntityExtension.FlagForDelete(model, _identityService.Username, _userAgent);
            _dbSet.Update(model);

            var itemModels = _itemDbSet.Where(item => item.OthersExpenditureProofDocumentId == id).ToList();
            itemModels = itemModels.Select(item =>
            {
                EntityExtension.FlagForDelete(item, _identityService.Username, _userAgent);
                return item;
            }).ToList();
            _itemDbSet.UpdateRange(itemModels);

            await _dbContext.SaveChangesAsync();
            //await _autoJournalService.AutoJournalReverseFromOthersExpenditureProof(model.DocumentNo);
            //await _autoDailyBankTransactionService.AutoRevertFromOthersExpenditureProofDocument(model, itemModels);

            return _taskDone;
        }

        public async Task<List<int>> GetAccountBankIds(string keyword)
        {
            var http = _serviceProvider.GetService<IHttpClientService>();

            var response = await http.GetAsync(APIEndpoint.Core + $"master/account-banks?keyword={keyword}&size={int.MaxValue}");

            var responseString = await response.Content.ReadAsStringAsync();
            var jsonSerializationSetting = new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore };

            var result = JsonConvert.DeserializeObject<APIDefaultResponse<List<AccountBank>>>(responseString, jsonSerializationSetting);

            return result.data.Select(accountBank => accountBank.Id).ToList();
        }

        public async Task<OthersExpenditureProofPagedListViewModel> GetPagedListAsync(int page, int size, string order, string keyword, string filter)
        {
            var query = _dbSet.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var accountBankIds = await GetAccountBankIds(keyword);
                query = query.Where(document => document.Type.Contains(keyword) || document.DocumentNo.Contains(keyword) || accountBankIds.Contains(document.AccountBankId));
            }

            var filterDictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(filter);
            query = QueryHelper<OthersExpenditureProofDocumentModel>.Filter(query, filterDictionary);

            var orderDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(order);
            query = QueryHelper<OthersExpenditureProofDocumentModel>.Order(query, orderDictionary);


            var total = await query.CountAsync();
            var data = query.Skip((page - 1) * size).Take(size).Select(document => new OthersExpenditureProofListViewModel()
            {
                AccountBankId = document.AccountBankId,
                DocumentNo = document.DocumentNo,
                Type = document.Type,
                Id = document.Id,
                Date = document.Date,
                IsPosted = document.IsPosted
            }).ToList();

            data = data.Select(element =>
            {
                element.Total = _itemDbSet.Where(item => item.OthersExpenditureProofDocumentId == element.Id).Sum(item => item.Debit);
                return element;
            }).ToList();

            return new OthersExpenditureProofPagedListViewModel()
            {
                Data = data,
                Page = page,
                Size = size,
                Total = total,
                Count = data.Count
            };
        }

        public async Task<int> UpdateAsync(int id, OthersExpenditureProofDocumentCreateUpdateViewModel viewModel)
        {
            var model = await _dbSet.FirstOrDefaultAsync(x => x.Id == id);
            model.Update(viewModel);
            EntityExtension.FlagForUpdate(model, _identityService.Username, _userAgent);
            _dbSet.Update(model);

            var itemFormIds = viewModel.Items.Select(item => item.Id.GetValueOrDefault()).ToList();
            var itemModels = await _itemDbSet.Where(item => itemFormIds.Contains(item.Id)).ToListAsync();
            await _autoDailyBankTransactionService.AutoRevertFromOthersExpenditureProofDocument(model, itemModels);

            List<int> itemIds = await _itemDbSet.Where(w => w.OthersExpenditureProofDocumentId.Equals(id) && !w.IsDeleted).Select(s => s.Id).ToListAsync();
            var itemModelsToUpdate = viewModel.MapItemToModel();

            foreach (var itemId in itemIds)
            {
                var item = itemModels.FirstOrDefault(f => f.Id.Equals(itemId));
                if (item == null)
                {
                    var itemToDelete = await _itemDbSet.FirstOrDefaultAsync(f => f.Id.Equals(itemId));
                    EntityExtension.FlagForDelete(itemToDelete, _identityService.Username, _userAgent);
                    _itemDbSet.Update(itemToDelete);
                }
                else
                {
                    var itemModelToUpdate = itemModelsToUpdate.FirstOrDefault(f => f.Id == itemId);
                    item.UpdateCOAId(itemModelToUpdate.COAId);
                    item.UpdateDebit(itemModelToUpdate.Debit);
                    item.UpdateRemark(itemModelToUpdate.Remark);
                    EntityExtension.FlagForUpdate(item, _identityService.Username, _userAgent);
                    _itemDbSet.Update(item);
                }
            }

            foreach (var item in itemModelsToUpdate)
            {
                if (item.Id <= 0)
                {
                    EntityExtension.FlagForCreate(item, _identityService.Username, _userAgent);
                    _itemDbSet.Add(item);
                }
            }

            return await _dbContext.SaveChangesAsync();
        }

        public async Task<OthersExpenditureProofDocumentViewModel> GetSingleByIdAsync(int id)
        {
            var model = await _dbSet.AsNoTracking().FirstOrDefaultAsync(document => document.Id == id);
            var items = _itemDbSet.AsNoTracking().Where(item => item.OthersExpenditureProofDocumentId == id).ToList();

            return new OthersExpenditureProofDocumentViewModel(model, items);
        }

        public async Task<OthersExpenditureProofDocumentPDFViewModel> GetPDFByIdAsync(int id)
        {
            var model = await _dbSet.AsNoTracking().FirstOrDefaultAsync(document => document.Id == id);
            var items = _itemDbSet.AsNoTracking().Where(item => item.OthersExpenditureProofDocumentId == id).ToList();
            var coaIds = items.Select(element => element.COAId).ToList();
            var coas = _dbContext.ChartsOfAccounts.Where(element => coaIds.Contains(element.Id)).ToList();

            var accountBank = await GetAccountBank(model.AccountBankId);

            return new OthersExpenditureProofDocumentPDFViewModel(model, items, accountBank, coas);
        }

        private async Task<AccountBankViewModel> GetAccountBank(int bankId)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            var http = _serviceProvider.GetService<IHttpClientService>();
            var uri = APIEndpoint.Core + $"master/account-banks/{bankId}";
            var response = await http.GetAsync(uri);

            var result = new BaseResponse<AccountBankViewModel>();

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                result = JsonConvert.DeserializeObject<BaseResponse<AccountBankViewModel>>(responseContent, jsonSerializerSettings);
            }
            return result.data;
        }

        public async Task<int> Posting(List<int> ids)
        {
            var models = _dbContext.OthersExpenditureProofDocuments.Where(entity => ids.Contains(entity.Id)).ToList();
            var itemModels = _dbContext.OthersExpenditureProofDocumentItems.Where(entity => ids.Contains(entity.OthersExpenditureProofDocumentId)).ToList();

            foreach (var model in models)
            {
                var items = itemModels.Where(element => element.OthersExpenditureProofDocumentId == model.Id).ToList();
                model.IsPosted = true;
                EntityExtension.FlagForUpdate(model, _identityService.Username, _userAgent);

                await _autoJournalService.AutoJournalFromOthersExpenditureProof(model, items);
                await _autoDailyBankTransactionService.AutoCreateFromOthersExpenditureProofDocument(model, items);
            }

            var result = await _dbContext.SaveChangesAsync();

            return result;
        }
    }
}