export interface PdfSettings {
  format: string;
  marginTop: string;
  marginBottom: string;
  marginLeft: string;
  marginRight: string;
  documentTitle: string;
  dateTimeFormat: string;
}

// Future PDF settings to be re-added:
// enableHeader: boolean;
// documentSubtitle: string;
// headerCenter: string;
// author: string;
// enableFooter: boolean;
// footerCenter: string;
// company: string;
// project: string;
// showPageNumbers: boolean;
// orientation: string;
// printBackground: boolean;
// scale: number;

export const DEFAULT_PDF_SETTINGS: Readonly<PdfSettings> = {
  format: 'Letter',
  marginTop: '0.75in',
  marginBottom: '0.75in',
  marginLeft: '0.5in',
  marginRight: '0.5in',
  documentTitle: '',
  dateTimeFormat: 'M/d/yyyy h:mm tt',
};
