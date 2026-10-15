CREATE TABLE t_receipts( 
  seq integer NOT NULL
  , date text NOT NULL
  , shop_name text NOT NULL
  , shop_location text
  , tax_type integer NOT NULL
  , payment_classification_code integer NOT NULL
  , tag text
  , update_user text NOT NULL
  , update_datetime text NOT NULL
  , total integer
  
  ,primary key (seq)
  
  ,FOREIGN KEY(payment_classification_code) REFERENCES m_payment_classification(code)
 );
CREATE TABLE m_payment_classification( 
  code integer not null
  , name text not null
  , seq integer
  , default_point_code integer
  , user_code integer references m_user(code), valid_from date, valid_to date, primary key (code)
  , foreign key (default_point_code) REFERENCES "m_point_classification"(code)
);
CREATE TABLE "m_point_classification"( 
  code integer not null
  , name text not null
  , seq integer
  , default_rate real
  , primary key (code)
);
CREATE TABLE t_receipt_points( 
  receipt_seq integer NOT NULL
  , seq integer NOT NULL
  , code integer NOT NULL
  , rate real NOT NULL
  , fixed_points integer NOT NULL
  , total_points number, primary key (seq)
  , FOREIGN KEY (code) REFERENCES "m_point_classification"(code)
);
CREATE TABLE t_transfers( 
  seq integer NOT NULL
  , date text NOT NULL
  , name text
  , fee integer NOT NULL
  , from_payment_classification_code integer NOT NULL
  , to_payment_classification_code integer NOT NULL
  , tag text
  , update_user text NOT NULL
  , update_datetime text NOT NULL
  , primary key (seq)
  , FOREIGN KEY (from_payment_classification_code) REFERENCES m_payment_classification(code)
  , FOREIGN KEY (to_payment_classification_code) REFERENCES m_payment_classification(code)
);
CREATE TABLE m_group_classification( 
  code integer not null
  , name text not null
  , seq integer
  , default_tax_target integer not null
  , reduced_tax_target integer not null default 0, primary key (code)
);
CREATE TABLE t_receipt_groups( 
  receipt_seq integer NOT NULL
  , seq integer NOT NULL
  , group_classification_code integer NOT NULL
  , tag text
  , tax integer not null default 8, primary key (receipt_seq, seq)
  , FOREIGN KEY (receipt_seq) REFERENCES t_receipts(seq)
  , FOREIGN KEY (group_classification_code) REFERENCES m_group_classification(code)
);
CREATE TABLE t_receipt_details( 
  receipt_seq integer NOT NULL
  , group_seq integer NOT NULL
  , seq integer NOT NULL
  , name text NOT NULL
  , fee integer NOT NULL
  , quantity integer NOT NULL
  , discount_fee integer NOT NULL
  , discount_rate integer NOT NULL
  , tax_target integer NOT NULL
  , tag text
  , primary key (receipt_seq, group_seq, seq)
  , FOREIGN KEY (receipt_seq, group_seq) REFERENCES t_receipt_groups (receipt_seq, seq)
);
CREATE TABLE m_shop( 
  code integer not null
  , name text not null
  , seq integer
  , default_point_code integer
  , default_group_classification_code integer
  , user_code integer references m_user(code), primary key (code)
  , foreign key (default_point_code) REFERENCES "m_point_classification" (code)
  , foreign key (default_group_classification_code) REFERENCES "m_group_classification" (code)
);
CREATE TABLE m_tax_rate( 
  code integer not null
  , rate integer not null
  , start_date date
  , primary key (code)
);
CREATE TABLE t_balances( 
  seq integer NOT NULL
  , date text NOT NULL
  , tag text
  , update_user text NOT NULL
  , update_datetime text NOT NULL
  , primary key (seq)
);
CREATE TABLE t_balance_groups( 
  balance_seq integer NOT NULL
  , payment_classification_code integer NOT NULL
  , balance integer NOT NULL
  , tag text
  , valuation_amount integer, primary key (balance_seq, payment_classification_code)
  , FOREIGN KEY (balance_seq) REFERENCES t_balances(seq)
  , FOREIGN KEY (payment_classification_code) REFERENCES m_payment_classification(code)
);
CREATE TABLE m_user( 
  code integer not null
  , name text not null
  , account_name text not null
  , seq integer
  , primary key (code)
);
CREATE TABLE t_receipt_detail_users (
  receipt_seq integer NOT NULL
  , group_seq integer NOT NULL
  , detail_seq integer NOT NULL
  , user_code integer NOT NULL
  , primary key (receipt_seq, group_seq, detail_seq, user_code)
  , FOREIGN KEY (receipt_seq, group_seq, detail_seq) REFERENCES t_receipt_details(receipt_seq, group_seq, seq)
  , foreign key (user_code) references m_user(code)
);
CREATE TABLE "t_payment_memo" ( 
  payment_classification_code integer not null
  , check_date date
  , tag text
  , primary key (payment_classification_code)
  , FOREIGN KEY (payment_classification_code) REFERENCES m_payment_classification(code)
);
CREATE TABLE t_exchange_rates (
  rate_date text not null
  , base_currency text not null
  , quote_currency text not null
  , middle_rate real not null
  , PRIMARY KEY (rate_date, base_currency, quote_currency)
);
