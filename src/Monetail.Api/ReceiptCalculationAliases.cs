// 計算用型は共通ライブラリに集約し、HTTP/保存処理から利用する。
global using ReceiptCalculationInput = Monetail.Calculations.Receipts.ReceiptCalculationInput;
global using ReceiptCalculationGroup = Monetail.Calculations.Receipts.ReceiptCalculationGroup;
global using ReceiptCalculationDetail = Monetail.Calculations.Receipts.ReceiptCalculationDetail;
global using ReceiptAmounts = Monetail.Calculations.Receipts.ReceiptAmounts;
global using ReceiptGroupAmounts = Monetail.Calculations.Receipts.ReceiptGroupAmounts;
global using ReceiptDetailAmounts = Monetail.Calculations.Receipts.ReceiptDetailAmounts;
global using ReceiptTaxSubtotal = Monetail.Calculations.Receipts.ReceiptTaxSubtotal;
global using ReceiptValidationException = Monetail.Calculations.Receipts.ReceiptValidationException;
