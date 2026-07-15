export interface PdfSettings {
  format: string;
  orientation: string;
  marginTop: string;
  marginBottom: string;
  marginLeft: string;
  marginRight: string;
  documentTitle: string;
  documentSubtitle: string;
  headerCenter: string;
  footerCenter: string;
  author: string;
  company: string;
  project: string;
  enableHeader: boolean;
  enableFooter: boolean;
  showPageNumbers: boolean;
  printBackground: boolean;
  scale: number;
  dateTimeFormat: string;
}

export const DEFAULT_PDF_SETTINGS: Readonly<PdfSettings> = {
  format: 'Letter',
  orientation: 'portrait',
  marginTop: '0.75in',
  marginBottom: '0.75in',
  marginLeft: '0.5in',
  marginRight: '0.5in',
  documentTitle: '',
  documentSubtitle: '',
  headerCenter: '',
  footerCenter: '',
  author: '',
  company: '',
  project: '',
  enableHeader: true,
  enableFooter: true,
  showPageNumbers: true,
  printBackground: true,
  scale: 1.0,
  dateTimeFormat: 'M/d/yyyy h:mm tt',
};
