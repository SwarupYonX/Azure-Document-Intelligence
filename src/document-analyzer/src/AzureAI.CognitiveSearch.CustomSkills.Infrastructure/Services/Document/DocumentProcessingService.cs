﻿using AzureAI.CognitiveSearch.CustomSkills.Infrastructure.Constants;
using AzureAI.CognitiveSearch.CustomSkills.Infrastructure.Model;
using AzureAI.CognitiveSearch.CustomSkills.Infrastructure.Services.Data.Interfaces;
using AzureAI.CognitiveSearch.CustomSkills.Infrastructure.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AzureAI.CognitiveSearch.CustomSkills.Infrastructure.Services.Document
{
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly IDocumentContentExtractor _documentContentExtractor;
        private readonly IFormRecognizerService _formRecognizerService;
        private readonly IDataService<InvoiceData> _dataService;
        private readonly ILogger<DocumentProcessingService> _log;

        public DocumentProcessingService(IDocumentContentExtractor documentContentExtractor,
                                                                IFormRecognizerService formRecognizerService,
                                                                IDataService<InvoiceData> dataService,
                                                                ILogger<DocumentProcessingService> log)
        {
            _documentContentExtractor = documentContentExtractor;
            _formRecognizerService = formRecognizerService;
            _dataService = dataService;
            _log = log;
        }

        public async Task<IList<WebApiRequestRecord>> DeserializeRequestAsync(HttpRequest request)
        {
            using (StreamReader reader = new StreamReader(request.Body))
            {
                string jsonRequest = await reader.ReadToEndAsync();
                WebApiSkillRequest docs = JsonConvert.DeserializeObject<WebApiSkillRequest>(jsonRequest);
                return docs.Values;
            }
        }

        public async Task<WebApiSkillResponse> ProcessInvoicesRecordsAsync(IEnumerable<WebApiRequestRecord> requestRecords)
        {
            WebApiSkillResponse response = new WebApiSkillResponse();

            foreach (WebApiRequestRecord inRecord in requestRecords)
            {
                WebApiResponseRecord outRecord = new WebApiResponseRecord()
                {
                    RecordId = inRecord.RecordId
                };

                try
                {
                    outRecord = await ProcessInvoiceRecord(inRecord, outRecord);
                }

                catch (Exception e)
                {
                    var erorMessage = $"{ServiceConstants.FormAnalyzerServiceName} - Error processing the request record: {e.ToString() }";
                    outRecord.Errors.Add(new WebApiErrorWarningContract()
                    {
                        Message = erorMessage
                    });

                    _log.LogError(erorMessage);
                }
                response.Values.Add(outRecord);
            }

            return response;
        }

        private async Task<WebApiResponseRecord> ProcessInvoiceRecord(WebApiRequestRecord webApiRequestRecord,
                                                                        WebApiResponseRecord webApiResponseRecord)
        {
            var formUrl = webApiRequestRecord.Data["formUrl"] as string;

            _log.LogInformation($"{ServiceConstants.FormAnalyzerServiceName} - Got form URL: {formUrl}");

            var analysisResult = await ProcessInvoiceDocumentContent(formUrl);

            webApiResponseRecord.Data = new Dictionary<string, object>();
            var invoiceData = new InvoiceData();

            if (analysisResult.documentResults != null)
            {
                var documents = analysisResult.documentResults;
                foreach (var documentResult in documents)
                {
                    var documentFields = documentResult.fields;
                    if (documentFields != null)
                    {
                        if (documentFields.Charges != null)
                        {
                            webApiResponseRecord.Data.Add(documentFields.Charges.fieldName, documentFields.Charges.text);
                            invoiceData.Charges = documentFields.Charges.text;
                        }
                        else
                        {
                            _log.LogWarning($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get field: 'Charges' for the form with URL: {formUrl}");
                        }
                        if (documentFields.ForCompany != null)
                        {
                            webApiResponseRecord.Data.Add(documentFields.ForCompany.fieldName, documentFields.ForCompany.text);
                            invoiceData.ForCompany = documentFields.ForCompany.text;
                        }
                        else
                        {
                            _log.LogWarning($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get field: 'ForCompany' for the form with URL: {formUrl}");
                        }
                        if (documentFields.FromCompany != null)
                        {
                            webApiResponseRecord.Data.Add(documentFields.FromCompany.fieldName, documentFields.FromCompany.text);
                            invoiceData.FromCompany = documentFields.FromCompany.text;
                        }
                        else
                        {
                            _log.LogWarning($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get field: 'FromCompany' for the form with URL: {formUrl}");
                        }
                        if (documentFields.InvoiceDate != null)
                        {
                            webApiResponseRecord.Data.Add(documentFields.InvoiceDate.fieldName, documentFields.InvoiceDate.text);
                            invoiceData.InvoiceDate = documentFields.InvoiceDate.text;
                        }
                        else
                        {
                            _log.LogWarning($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get field: 'InvoiceDate' for the form with URL: {formUrl}");
                        }
                        if (documentFields.InvoiceDueDate != null)
                        {
                            webApiResponseRecord.Data.Add(documentFields.InvoiceDueDate.fieldName, documentFields.InvoiceDueDate.text);
                            invoiceData.InvoiceDueDate = documentFields.InvoiceDueDate.text;
                        }
                        else
                        {
                            _log.LogWarning($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get field: 'InvoiceDueDate' for the form with URL: {formUrl}");
                        }
                        if (documentFields.InvoiceNumber != null)
                        {
                            webApiResponseRecord.Data.Add(documentFields.InvoiceNumber.fieldName, documentFields.InvoiceNumber.text);
                            invoiceData.InvoiceNumber = documentFields.InvoiceNumber.text;
                        }
                        else
                        {
                            _log.LogWarning($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get field: 'InvoiceNumber' for the form with URL: {formUrl}");
                        }
                        if (documentFields.VatID != null)
                        {
                            webApiResponseRecord.Data.Add(documentFields.VatID.fieldName, documentFields.VatID.text);
                            invoiceData.VatID = documentFields.VatID.text;
                        }
                        else
                        {
                            _log.LogWarning($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get field: 'VatID' for the form with URL: {formUrl}");
                        }
                    }
                    else
                    {
                        _log.LogError($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get any fields from the form with URL: {formUrl}");
                    }
                }
            }
            else
            {
                _log.LogError($"{ServiceConstants.FormAnalyzerServiceName} - Cannot get any document results from the form with URL: {formUrl}");
            }

            await _dataService.AddAsync(invoiceData);
            return webApiResponseRecord;
        }

        private async Task<AnalyzeResult> ProcessInvoiceDocumentContent(string documentUrl)
        {
            var document = await _documentContentExtractor.DownloadDocumentAsync(documentUrl);
            if (document != null)
            {
                var formAnalysisResultEndpoint = await _formRecognizerService.AnalyzeFormAsync(document, documentUrl);
                if (!string.IsNullOrEmpty(formAnalysisResultEndpoint))
                {
                    Task delay = Task.Delay(5000);
                    await delay;
                    var formAnalysisResult = await _formRecognizerService.GetFormAnalysisResultAsync(formAnalysisResultEndpoint);
                    if (formAnalysisResult != null)
                    {
                        return formAnalysisResult.analyzeResult;

                    }
                }
            }

            return null;
        }
    }
}
