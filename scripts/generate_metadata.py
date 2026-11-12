#!/usr/bin/env python3
"""Internal helper: produce YAML-1.2-valid JSON syntax as dependency-free metadata.

This simple starter intentionally uses JSON syntax inside .yaml files. YAML 1.2
includes JSON; Python's standard-library json module can therefore validate
these documents with no PyYAML dependency.
"""
import argparse
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description='Bootstrap-only metadata generator; does not inspect current domain code.')
parser.add_argument('--force', action='store_true', help='Explicitly overwrite existing metadata (destructive; use only after review)')
options = parser.parse_args()
if not options.force and (root / 'contract-catalog.yaml').exists():
    print('Refusing to overwrite existing ABA metadata. This is a bootstrap seed generator, not a sync tool. '
          'Update owning context docs and catalog deliberately; --force only after explicit review.', file=sys.stderr)
    sys.exit(2)
contexts = {
  'Identity': {
    'data': ['User'], 'rules': ['NormalizeEmail','UniqueEmail'], 'decisions': ['RegisterUser'],
    'contracts': ['UserRegisteredV1','UserViewV1','IIdentityReaderV1'],
    'publishes': ['UserRegisteredV1'], 'consumes': [],
    'deps': ['Aba.Foundation','Aba.LocalMessaging','Academy.Identity.Contracts'],
    'capabilities': ['User lifecycle','User registration']
  },
  'Enrollment': {
    'data': ['Course','Enrollment'], 'rules': ['CourseCapacity','PaidEnrollment'],
    'decisions': ['ActivateEnrollment','WaitlistEnrollment'],
    'contracts': ['EnrollmentRequestedV1','EnrollmentActivatedV1','EnrollmentWaitlistedV1',
                  'CourseViewV1','EnrollmentViewV1','PaymentQuoteV1','IEnrollmentPaymentQuoteV1'],
    'publishes': ['EnrollmentRequestedV1','EnrollmentActivatedV1','EnrollmentWaitlistedV1'],
    'consumes': ['PaymentCapturedV1'],
    'deps': ['Aba.Foundation','Aba.LocalMessaging','Academy.Enrollment.Contracts',
             'Academy.Identity.Contracts','Academy.Payments.Contracts'],
    'capabilities': ['Course catalog','Participation and capacity']
  },
  'Payments': {
    'data': ['Payment'], 'rules': ['PaymentIdempotency','SimulatedPaymentCapture'],
    'decisions': ['CapturePayment'],
    'contracts': ['PaymentCapturedV1','PaymentViewV1'],
    'publishes': ['PaymentCapturedV1'], 'consumes': [],
    'deps': ['Aba.Foundation','Aba.LocalMessaging','Academy.Payments.Contracts','Academy.Enrollment.Contracts'],
    'capabilities': ['Simulated payment execution','Payment idempotency']
  },
  'Accounting': {
    'data': ['Journal','JournalLine'], 'rules': ['BalancedDebitsAndCredits','ImmutablePostedJournals'],
    'decisions': ['PostJournal'],
    'contracts': ['JournalPostedV1','JournalLineViewV1','JournalViewV1'],
    'publishes': ['JournalPostedV1'], 'consumes': ['PaymentCapturedV1'],
    'deps': ['Aba.Foundation','Aba.LocalMessaging','Academy.Accounting.Contracts','Academy.Payments.Contracts'],
    'capabilities': ['Balanced financial journal posting','Append-only financial truth']
  },
  'Reporting': {
    'data': ['ReportingProjection'], 'rules': ['ProjectionNotSourceOfTruth'],
    'decisions': ['UpdateReadModel'],
    'contracts': ['ReportSummaryV1'],
    'publishes': [],
    'consumes': ['UserRegisteredV1','EnrollmentRequestedV1','EnrollmentActivatedV1',
                 'EnrollmentWaitlistedV1','PaymentCapturedV1','JournalPostedV1'],
    'deps': ['Aba.Foundation','Aba.LocalMessaging','Aba.Caching','Academy.Reporting.Contracts',
             'Academy.Identity.Contracts','Academy.Enrollment.Contracts','Academy.Payments.Contracts',
             'Academy.Accounting.Contracts'],
    'capabilities': ['Event-driven reporting projections']
  },
}

def write_yaml_json(path, obj):
  path.write_text(json.dumps(obj, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')

catalog = {'contracts': {}}
for ctx, meta in contexts.items():
  docs = root/'samples'/'Academy'/ctx/'_Docs'
  docs.mkdir(parents=True, exist_ok=True)
  ownership = {
    'context': ctx,
    'data_ownership': {name: {'owner':ctx} for name in meta['data']},
    'rule_ownership': {name: {'owner':ctx} for name in meta['rules']},
    'decision_ownership': {name: {'owner':ctx} for name in meta['decisions']},
    'contract_ownership': {name: {'owner':ctx} for name in meta['contracts']},
    'forbidden_writers': [x for x in contexts if x != ctx],
  }
  cmap = {
    'context': ctx,
    'owned_capabilities': meta['capabilities'],
    'publishes': meta['publishes'],
    'consumes': meta['consumes'],
    'allowed_dependencies': meta['deps'],
    'forbidden_dependencies': [f'Academy.{x}' for x in contexts if x != ctx],
  }
  ai_rules = {
    'must_read':['ownership.yaml','context-map.yaml','decisions.md'],
    'forbidden':['create_cross_context_domain_reference','create_unversioned_contract',
                 'modify_other_context_database','share_business_domain_models'],
    'required_checks':['python scripts/governance.py','dotnet run --project tests/Aba.SmokeTests'],
  }
  write_yaml_json(docs/'ownership.yaml', ownership)
  write_yaml_json(docs/'context-map.yaml', cmap)
  write_yaml_json(docs/'ai-rules.yaml', ai_rules)
  (docs/'decisions.md').write_text(f'# {ctx}: ownership decisions\n\n{ctx} owns its listed business capabilities. External consumers depend only on published contracts.\n')
  (docs/'evolution.md').write_text(f'# {ctx}: evolution\n\nInitial phase-1 local development implementation. Replace file persistence with context-scoped PostgreSQL without exposing storage to other contexts.\n')
  for name in meta['contracts']:
    consumers = [c for c, v in contexts.items() if name in v['consumes']]
    catalog['contracts'][name] = {
      'owner':ctx,
      'type': 'IntegrationEvent' if name in meta['publishes'] else ('ApplicationQuery' if name.startswith('I') else 'DataTransfer'),
      'version': 1,
      'consumers':consumers,
      'compatibility':'additive_only',
      'status':'active',
    }
write_yaml_json(root/'contract-catalog.yaml', catalog)
print('Metadata generated for',len(contexts),'business contexts')
